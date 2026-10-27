using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Markup;

namespace MicroWinUI
{
    /// <summary>
    /// 在运行时用 <see cref="XamlReader"/> 加载页面，并模拟 XAML 编译器生成的行为：
    /// 按 x:Name 回填代码隐藏类的字段、按 XAML 中声明的事件属性挂接处理方法。
    ///
    /// 这样页面写法与标准 XAML 工程完全一致，加载器本身不依赖任何具体控件或页面。
    /// </summary>
    public static class XamlPageLoader
    {
        private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        /// <summary>
        /// 用嵌入资源中的 XAML 初始化页面。
        /// </summary>
        /// <param name="page">要初始化的页面实例。</param>
        /// <param name="xamlFileName">嵌入资源文件名，例如 "MainPage.xaml"。</param>
        public static void Initialize(Page page, string xamlFileName)
        {
            if (page == null)
            {
                throw new ArgumentNullException(nameof(page));
            }

            string xaml = ReadEmbeddedText(xamlFileName)
                ?? throw new InvalidOperationException($"未找到嵌入资源 {xamlFileName}。");

            XDocument document = XDocument.Parse(xaml);
            XElement rootElement = document.Root;

            // 事件属性在 XamlReader 里不会被解析，先摘出来，稍后按方法名挂接。
            // 没有 x:Name 的元素补一个临时名字，便于加载后在树里定位。
            var eventBindings = ExtractEventBindings(page.GetType(), rootElement);

            // XamlReader 不接受 x:Class，只能在 LoadComponent 场景使用。
            rootElement.Attributes()
                .Where(a => a.Name.LocalName == "Class" && a.Name.NamespaceName == XamlNamespace)
                .Remove();

            // 带事件的元素上若把布尔属性设为 True（如 IsChecked），
            // XAML 编译器会在解析期触发对应事件；XamlReader 不会。
            // 先把这些属性摘掉，等处理器挂好后再赋值，让事件照常触发。
            var deferredProperties = ExtractDeferredBooleanProperties(rootElement, eventBindings);

            var loaded = (Page)XamlReader.Load(document.ToString());


            BindNamedFields(page, loaded, rootElement);
            AttachEventHandlers(page, loaded, eventBindings);
            TransferContent(page, loaded);
            ApplyDeferredProperties(page, deferredProperties);
        }

        /// <summary>
        /// 把 XAML 根元素上的外观属性与内容转移到目标页面上。
        /// </summary>
        private static void TransferContent(Page page, Page loaded)
        {
            page.Background = loaded.Background;
            page.Padding = loaded.Padding;

            // 内容已被 loaded 持有，必须先摘下才能挂到目标页面。
            UIElement content = loaded.Content;
            loaded.Content = null;
            page.Content = content;
        }

        /// <summary>
        /// 扫描 XAML，找出所有能对应到代码隐藏类方法的属性（即事件处理器）。
        /// 为没有 x:Name 的元素补上临时名字，并从文档中移除这些属性。
        /// </summary>
        private static List<EventBinding> ExtractEventBindings(Type pageType, XElement rootElement)
        {
            var bindings = new List<EventBinding>();
            int autoNameIndex = 0;

            foreach (XElement element in rootElement.DescendantsAndSelf())
            {
                string elementName = GetXName(element);

                foreach (XAttribute attribute in element.Attributes().ToList())
                {
                    // 跳过 x:* 这类指令属性。普通属性（含事件）继承默认命名空间，
                    // 因此不能简单地按「无命名空间」过滤。
                    if (attribute.Name.NamespaceName == XamlNamespace)
                    {
                        continue;
                    }

                    MethodInfo method = pageType.GetMethod(
                        attribute.Value,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);


                    if (method == null || !IsEventHandler(method))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(elementName))
                    {
                        elementName = "__xamlAuto" + autoNameIndex++;
                        element.SetAttributeValue(XName.Get("Name", XamlNamespace), elementName);
                    }

                    bindings.Add(new EventBinding(elementName, attribute.Name.LocalName, method));
                    attribute.Remove();
                }
            }

            return bindings;
        }

        /// <summary>
        /// 找出「带事件处理器的元素」上值为 True 的布尔属性（典型是 IsChecked），
        /// 从 XAML 中移除并延迟到事件挂接之后再设置。
        /// </summary>
        private static List<DeferredProperty> ExtractDeferredBooleanProperties(
            XElement rootElement,
            List<EventBinding> bindings)
        {
            var deferred = new List<DeferredProperty>();

            foreach (string elementName in bindings.Select(b => b.ElementName).Distinct())
            {
                XElement element = rootElement
                    .DescendantsAndSelf()
                    .FirstOrDefault(e => GetXName(e) == elementName);

                if (element == null)
                {
                    continue;
                }

                foreach (XAttribute attribute in element.Attributes().ToList())
                {
                    if (attribute.Name.NamespaceName == XamlNamespace)
                    {
                        continue;
                    }

                    if (!string.Equals(attribute.Value, "True", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    deferred.Add(new DeferredProperty(elementName, attribute.Name.LocalName, attribute.Value));
                    attribute.Remove();
                }
            }

            return deferred;
        }

        /// <summary>
        /// 在事件处理器挂接完成后，重新赋上被延迟的布尔属性，触发对应事件。
        /// </summary>
        private static void ApplyDeferredProperties(Page loaded, List<DeferredProperty> deferred)
        {
            foreach (DeferredProperty property in deferred)
            {
                DependencyObject target = FindByName(loaded, property.ElementName);
                if (target == null)
                {
                    continue;
                }

                PropertyInfo propertyInfo = target.GetType().GetProperty(property.PropertyName);
                if (propertyInfo == null || !propertyInfo.CanWrite)
                {
                    continue;
                }

                // 属性可能是 Nullable<T>（如 ToggleButton.IsChecked 是 bool?），
                // Convert.ChangeType 无法直接处理，需要先取出底层类型。
                Type valueType = Nullable.GetUnderlyingType(propertyInfo.PropertyType)
                    ?? propertyInfo.PropertyType;

                object value = Convert.ChangeType(property.Value, valueType);
                propertyInfo.SetValue(target, value);
            }
        }

        private static bool IsEventHandler(MethodInfo method)
        {
            if (method.ReturnType != typeof(void))
            {
                return false;
            }

            // WinRT 的事件参数类型（如 Windows.UI.Xaml.RoutedEventArgs）
            // 并不继承 System.EventArgs，所以只能按签名形状判断。
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 2
                && parameters[0].ParameterType == typeof(object);
        }

        /// <summary>
        /// 按 x:Name 把加载出来的元素回填到页面实例的同名字段上。
        /// </summary>
        private static void BindNamedFields(Page page, Page loaded, XElement rootElement)
        {
            Type pageType = page.GetType();

            foreach (XElement element in rootElement.DescendantsAndSelf())
            {
                string name = GetXName(element);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                FieldInfo field = pageType.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (field == null)
                {
                    continue;
                }

                object target = FindByName(loaded, name);
                if (target != null && field.FieldType.IsInstanceOfType(target))
                {
                    field.SetValue(page, target);
                }
            }
        }

        /// <summary>
        /// 把 XAML 中声明的事件处理器挂接到对应元素上。
        /// </summary>
        private static void AttachEventHandlers(Page page, Page loaded, List<EventBinding> bindings)
        {
            foreach (EventBinding binding in bindings)
            {
                object target = FindByName(loaded, binding.ElementName);
                if (target == null)
                {
                    continue;
                }

                EventInfo eventInfo = target.GetType().GetEvent(binding.EventName);
                if (eventInfo == null)
                {
                    continue;
                }

                Delegate handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, page, binding.Method, false);
                if (handler == null)
                {
                    continue;
                }

                // WinRT 事件的 add 访问器返回 EventRegistrationToken 而非 void，
                // EventInfo.AddEventHandler 会因此抛出 InvalidOperationException，
                // 所以这里直接调用 add 方法。
                MethodInfo addMethod = eventInfo.GetAddMethod(true);
                if (addMethod != null)
                {
                    addMethod.Invoke(target, new object[] { handler });
                }
            }
        }

        private static string GetXName(XElement element)
        {
            return element.Attribute(XName.Get("Name", XamlNamespace))?.Value;
        }

        /// <summary>
        /// 在逻辑树中按名称查找元素。
        /// 注意：此时元素尚未进入可视树，只能用逻辑树，不能用 VisualTreeHelper。
        /// </summary>
        public static DependencyObject FindByName(DependencyObject root, string name)
        {
            foreach (DependencyObject node in EnumerateSelfAndDescendants(root))
            {
                if (node is FrameworkElement frameworkElement && frameworkElement.Name == name)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// 深度优先枚举自身及全部后代元素。
        /// </summary>
        public static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (DependencyObject node in EnumerateSelfAndDescendants(root))
            {
                if (node is T match)
                {
                    yield return match;
                }
            }
        }

        private static IEnumerable<DependencyObject> EnumerateSelfAndDescendants(DependencyObject root)
        {
            if (root == null)
            {
                yield break;
            }

            yield return root;

            foreach (DependencyObject child in GetChildren(root))
            {
                foreach (DependencyObject descendant in EnumerateSelfAndDescendants(child))
                {
                    yield return descendant;
                }
            }
        }

        private static IEnumerable<DependencyObject> GetChildren(DependencyObject node)
        {
            if (node is Panel panel)
            {
                foreach (UIElement child in panel.Children)
                {
                    yield return child;
                }
            }
            else if (node is UserControl userControl && userControl.Content is DependencyObject userContent)
            {
                yield return userContent;
            }
            else if (node is ContentControl contentControl && contentControl.Content is DependencyObject content)
            {
                yield return content;
            }
            else if (node is ContentPresenter presenter && presenter.Content is DependencyObject presented)
            {
                yield return presented;
            }
            else if (node is Border border && border.Child is DependencyObject borderChild)
            {
                yield return borderChild;
            }
            else if (node is ItemsControl itemsControl)
            {
                foreach (object item in itemsControl.Items)
                {
                    if (item is DependencyObject itemObject)
                    {
                        yield return itemObject;
                    }
                }
            }
        }

        /// <summary>
        /// 读取以指定文件名结尾的嵌入资源文本。
        /// </summary>
        public static string ReadEmbeddedText(string fileName)
        {
            Assembly assembly = typeof(XamlPageLoader).Assembly;
            string normalizedFileName = fileName.Replace('\\', '/').TrimStart('/');

            foreach (string resourceName in assembly.GetManifestResourceNames())
            {
                string normalizedResourceName = resourceName.Replace('\\', '/');
                if (normalizedResourceName.EndsWith(normalizedFileName, StringComparison.OrdinalIgnoreCase))
                {
                    using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)
                        {
                            return null;
                        }

                        using (Stream content = DecompressIfNeeded(stream))
                        using (var reader = new StreamReader(content, System.Text.Encoding.UTF8))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 构建期把 XAML 以 gzip 形式嵌入以减小体积，这里按文件头识别并解压。
        /// 未压缩的资源（如运行时直接放置的）原样返回。
        /// </summary>
        private static Stream DecompressIfNeeded(Stream stream)
        {
            if (!stream.CanSeek)
            {
                return stream;
            }

            long origin = stream.Position;
            int first = stream.ReadByte();
            int second = stream.ReadByte();
            stream.Position = origin;

            // gzip 魔数：0x1F 0x8B
            if (first == 0x1F && second == 0x8B)
            {
                return new System.IO.Compression.GZipStream(
                    stream,
                    System.IO.Compression.CompressionMode.Decompress);
            }

            return stream;
        }

        private sealed class DeferredProperty
        {
            public DeferredProperty(string elementName, string propertyName, string value)
            {
                ElementName = elementName;
                PropertyName = propertyName;
                Value = value;
            }

            public string ElementName { get; }
            public string PropertyName { get; }
            public string Value { get; }
        }

        private sealed class EventBinding
        {
            public EventBinding(string elementName, string eventName, MethodInfo method)
            {
                ElementName = elementName;
                EventName = eventName;
                Method = method;
            }

            public string ElementName { get; }
            public string EventName { get; }
            public MethodInfo Method { get; }
        }
    }
}
