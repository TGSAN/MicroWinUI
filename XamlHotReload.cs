using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Markup;

namespace MicroWinUI
{
    /// <summary>
    /// 运行时加载页面的 XAML 热重载（仅本机 Debug）。
    ///
    /// 保存页面源文件后，对比新旧 XAML，把差异增量应用到正在运行的对象树上，
    /// 未改动的元素保持原实例与状态。无法增量处理时退回为整页替换。
    /// </summary>
    internal static class XamlHotReload
    {
        private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        private const string LogPrefix = "[XamlHotReload] ";

        private static readonly Lazy<string> SourceRoot = new Lazy<string>(() =>
        {
            string root = typeof(XamlHotReload).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "XamlHotReloadSourceRoot")?.Value;
            return !string.IsNullOrEmpty(root) && Directory.Exists(root) ? root : null;
        });

        private static readonly List<PageState> Pages = new List<PageState>();

        /// <summary>
        /// Debug 构建且源码目录存在（即本机调试）时启用。
        /// </summary>
        public static bool IsEnabled => SourceRoot.Value != null;

        public static void Register(Page page, string xamlFileName, XElement snapshot)
        {
            string path = Path.Combine(SourceRoot.Value, xamlFileName);
            if (!File.Exists(path))
            {
                return;
            }

            var state = new PageState(page, Path.GetFullPath(path), snapshot);
            lock (Pages)
            {
                Pages.Add(state);
            }

            Debug.WriteLine(LogPrefix + "监视 " + state.SourcePath);
        }

        private sealed class PageState
        {
            private readonly WeakReference<Page> page;
            private readonly FileSystemWatcher watcher;
            private readonly Timer timer;
            private readonly object sync = new object();
            private string lastText;

            public PageState(Page page, string sourcePath, XElement snapshot)
            {
                this.page = new WeakReference<Page>(page);
                SourcePath = sourcePath;
                Current = snapshot;
                PageType = page.GetType();

                timer = new Timer(_ => OnTimer(), null, Timeout.Infinite, Timeout.Infinite);
                watcher = new FileSystemWatcher(Path.GetDirectoryName(sourcePath), "*.xaml")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                watcher.Changed += OnFileEvent;
                watcher.Created += OnFileEvent;
                watcher.Renamed += OnFileEvent;
                watcher.EnableRaisingEvents = true;
            }

            public string SourcePath { get; }
            public Type PageType { get; }
            public XElement Current { get; set; }

            private void OnFileEvent(object sender, FileSystemEventArgs e)
            {
                bool hit = string.Equals(e.FullPath, SourcePath, StringComparison.OrdinalIgnoreCase)
                    || (e is RenamedEventArgs r && string.Equals(r.OldFullPath, SourcePath, StringComparison.OrdinalIgnoreCase));
                if (hit)
                {
                    // 编辑器保存时往往连续触发多次，去抖后再处理。
                    timer.Change(200, Timeout.Infinite);
                }
            }

            private void OnTimer()
            {
                if (!page.TryGetTarget(out Page target))
                {
                    watcher.Dispose();
                    timer.Dispose();
                    return;
                }

                string text = ReadWithRetry(SourcePath);
                if (text == null)
                {
                    return;
                }

                lock (sync)
                {
                    if (text == lastText)
                    {
                        return;
                    }

                    lastText = text;
                }

                _ = target.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () => Apply(this, target, text));
            }
        }

        private static string ReadWithRetry(string path)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    return File.ReadAllText(path);
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(50);
                }
            }

            return null;
        }

        private static void Apply(PageState state, Page page, string text)
        {
            XElement newRoot;
            try
            {
                newRoot = XDocument.Parse(text).Root;
                XamlPageLoader.ResolveConditionalNamespaces(newRoot);
                XamlPageLoader.RemoveClassDirective(newRoot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(LogPrefix + "XAML 格式错误，等待下次保存：" + ex.Message);
                return;
            }

            if (IsDeepEqual(state.Current, newRoot))
            {
                return;
            }

            Page loaded;
            try
            {
                loaded = LoadWithoutEvents(newRoot, state.PageType);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(LogPrefix + "XAML 解析失败，界面保持不变：" + ex.Message);
                return;
            }

            var context = new PatchContext(page, state.PageType);
            Page rootSource = loaded;
            try
            {
                PatchRoot(state.Current, newRoot, loaded, context);
                foreach (var pair in context.NewItems)
                {
                    AttachEvents(pair.Key, pair.Value, context);
                }

                Debug.WriteLine(LogPrefix + "已增量更新 " + Path.GetFileName(state.SourcePath));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(LogPrefix + "无法增量更新，整页替换（状态会丢失）：" + ex.Message);
                try
                {
                    Page fresh = LoadWithoutEvents(newRoot, state.PageType);
                    XamlPageLoader.TransferContent(page, fresh);
                    WalkPairs(newRoot, page, (xml, obj) =>
                    {
                        if (!ReferenceEquals(obj, page))
                        {
                            AttachEvents(xml, obj, context);
                        }
                    });
                    rootSource = fresh;
                }
                catch (Exception inner)
                {
                    Debug.WriteLine(LogPrefix + "整页替换失败：" + inner.Message);
                    return;
                }
            }

            PatchRootAttributes(state.Current, newRoot, rootSource, context);
            state.Current = newRoot;

            var names = new Dictionary<string, DependencyObject>();
            WalkPairs(newRoot, page, (xml, obj) =>
            {
                string name = xml.Attribute(XName.Get("Name", XamlNamespace))?.Value;
                if (!string.IsNullOrEmpty(name) && obj is DependencyObject d)
                {
                    names[name] = d;
                }
            });
            XamlPageLoader.BindNamedFields(page, newRoot,
                name => names.TryGetValue(name, out var found) ? found : XamlPageLoader.FindByName(page, name));
        }

        private static Page LoadWithoutEvents(XElement root, Type pageType)
        {
            var copy = new XElement(root);
            foreach (XElement element in copy.DescendantsAndSelf())
            {
                foreach (XAttribute attribute in element.Attributes().ToList())
                {
                    if (!attribute.IsNamespaceDeclaration
                        && attribute.Name.NamespaceName != XamlNamespace
                        && FindHandler(pageType, attribute.Value) != null)
                    {
                        attribute.Remove();
                    }
                }
            }

            return (Page)XamlReader.Load(copy.ToString());
        }

        private static MethodInfo FindHandler(Type pageType, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            MethodInfo method = pageType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return method != null && XamlPageLoader.IsEventHandler(method) ? method : null;
        }

        #region 对比与修补

        private sealed class PatchContext
        {
            public PatchContext(Page page, Type pageType)
            {
                Page = page;
                PageType = pageType;
            }

            public Page Page { get; }
            public Type PageType { get; }
            public List<KeyValuePair<XElement, object>> NewItems { get; } = new List<KeyValuePair<XElement, object>>();
        }

        private sealed class ReplaceNeededException : Exception
        {
            public ReplaceNeededException(string message) : base(message) { }
        }

        private static void PatchRoot(XElement oldRoot, XElement newRoot, Page loaded, PatchContext context)
        {
            var oldSlots = SplitSlots(oldRoot);
            var newSlots = SplitSlots(newRoot);
            string contentProperty = GetContentPropertyName(context.Page.GetType()) ?? "Content";

            foreach (string key in oldSlots.Keys.Union(newSlots.Keys).ToList())
            {
                var oldItems = GetSlot(oldSlots, key);
                var newItems = GetSlot(newSlots, key);
                if (SequenceDeepEqual(oldItems, newItems))
                {
                    continue;
                }

                string property = key.Length == 0 ? contentProperty : key.Substring(key.LastIndexOf('.') + 1);
                if (property != contentProperty)
                {
                    // 初次加载只转移了内容与 Background/Padding，根上其它属性元素不在运行中的页面上。
                    Debug.WriteLine(LogPrefix + "根元素的 " + key + " 变更不支持热重载，需重新启动。");
                    continue;
                }

                PatchSlot(context.Page, loaded, property, oldItems, newItems, context);
            }
        }

        private static void PatchRootAttributes(XElement oldRoot, XElement newRoot, Page source, PatchContext context)
        {
            var oldAttrs = PatchableAttributes(oldRoot).ToDictionary(a => a.Name, a => a.Value);
            var newAttrs = PatchableAttributes(newRoot).ToDictionary(a => a.Name, a => a.Value);

            foreach (XName name in oldAttrs.Keys.Union(newAttrs.Keys))
            {
                oldAttrs.TryGetValue(name, out string oldValue);
                newAttrs.TryGetValue(name, out string newValue);
                if (oldValue == newValue)
                {
                    continue;
                }

                string attributeName = name.LocalName;
                if (context.Page.GetType().GetEvent(attributeName) != null)
                {
                    XamlPageLoader.DetachEventHandler(context.Page, attributeName);
                    MethodInfo method = FindHandler(context.PageType, newValue);
                    if (method != null)
                    {
                        XamlPageLoader.AttachEventHandler(context.Page, context.Page, attributeName, method);
                    }

                    continue;
                }

                if (!ApplyAttribute(context.Page, source, attributeName, newValue != null))
                {
                    Debug.WriteLine(LogPrefix + "根元素属性 " + attributeName + " 无法热重载。");
                }
            }
        }

        private static void Patch(XElement oldXml, XElement newXml, object oldObj, object newObj, PatchContext context)
        {
            if (IsDeepEqual(oldXml, newXml))
            {
                return;
            }

            var oldAttrs = PatchableAttributes(oldXml).ToDictionary(a => a.Name, a => a.Value);
            var newAttrs = PatchableAttributes(newXml).ToDictionary(a => a.Name, a => a.Value);
            foreach (XName name in oldAttrs.Keys.Union(newAttrs.Keys))
            {
                oldAttrs.TryGetValue(name, out string oldValue);
                newAttrs.TryGetValue(name, out string newValue);
                if (oldValue == newValue)
                {
                    continue;
                }

                string attributeName = name.LocalName;
                if (oldObj.GetType().GetEvent(attributeName) != null)
                {
                    XamlPageLoader.DetachEventHandler(oldObj, attributeName);
                    MethodInfo method = FindHandler(context.PageType, newValue);
                    if (method != null)
                    {
                        XamlPageLoader.AttachEventHandler(context.Page, oldObj, attributeName, method);
                    }

                    continue;
                }

                if (!ApplyAttribute(oldObj, newObj, attributeName, newValue != null))
                {
                    throw new ReplaceNeededException($"{oldXml.Name.LocalName}.{attributeName} 无法增量设置");
                }
            }

            var oldSlots = SplitSlots(oldXml);
            var newSlots = SplitSlots(newXml);
            foreach (string key in oldSlots.Keys.Union(newSlots.Keys).ToList())
            {
                var oldItems = GetSlot(oldSlots, key);
                var newItems = GetSlot(newSlots, key);
                if (SequenceDeepEqual(oldItems, newItems))
                {
                    continue;
                }

                string property = ResolveSlotProperty(oldObj, key);
                PatchSlot(oldObj, newObj, property, oldItems, newItems, context);
            }
        }

        private static void PatchSlot(object oldOwner, object newOwner, string property,
            List<XNode> oldItems, List<XNode> newItems, PatchContext context)
        {
            PropertyInfo oldProp = FindProperty(oldOwner.GetType(), property);
            PropertyInfo newProp = FindProperty(newOwner.GetType(), property);
            if (oldProp == null || newProp == null)
            {
                throw new ReplaceNeededException("找不到属性 " + property);
            }

            ListAdapter oldList = ListAdapter.Create(oldProp.GetValue(oldOwner), oldProp.PropertyType);
            ListAdapter newList = ListAdapter.Create(newProp.GetValue(newOwner), newProp.PropertyType);

            if (oldList != null && newList != null && !OpaqueProperties.Contains(property))
            {
                ReconcileList(oldItems, newItems, oldList, newList, context);
                return;
            }

            PatchSingle(oldOwner, newOwner, oldProp, newProp, oldList, newList, oldItems, newItems, context);
        }

        private static void PatchSingle(object oldOwner, object newOwner, PropertyInfo oldProp, PropertyInfo newProp,
            ListAdapter oldList, ListAdapter newList, List<XNode> oldItems, List<XNode> newItems, PatchContext context)
        {
            object oldValue = oldProp.GetValue(oldOwner);
            object newValue = newProp.GetValue(newOwner);

            if (oldItems.Count == 1 && newItems.Count == 1
                && ItemKey(oldItems[0]) == ItemKey(newItems[0])
                && newItems[0] is XElement newElement
                && !IsOpaque(newElement)
                && oldValue != null && newValue != null
                && IsTypeMatch(newElement, oldValue)
                && !OpaqueProperties.Contains(oldProp.Name))
            {
                Patch((XElement)oldItems[0], newElement, oldValue, newValue, context);
                return;
            }

            if (oldProp.CanWrite)
            {
                if (newValue is UIElement && newProp.CanWrite)
                {
                    // UIElement 只能有一个父元素，先从新树上摘下。
                    try { newProp.SetValue(newOwner, null); } catch { }
                }

                try
                {
                    oldProp.SetValue(oldOwner, newValue);
                }
                catch (Exception ex)
                {
                    throw new ReplaceNeededException($"无法设置 {oldProp.Name}：{ex.Message}");
                }
            }
            else if (oldList != null && newList != null)
            {
                var values = Enumerable.Range(0, newList.Count).Select(newList.GetAt).ToList();
                newList.Clear();
                oldList.Clear();
                foreach (object value in values)
                {
                    oldList.Insert(oldList.Count, value);
                }
            }
            else
            {
                throw new ReplaceNeededException(oldProp.Name + " 只读，无法替换");
            }

            if (newItems.Count == 1 && newItems[0] is XElement inserted && newValue != null)
            {
                context.NewItems.Add(new KeyValuePair<XElement, object>(inserted, newValue));
            }
        }

        private static void ReconcileList(List<XNode> oldItems, List<XNode> newItems,
            ListAdapter oldList, ListAdapter newList, PatchContext context)
        {
            if (oldList.Count != oldItems.Count || newList.Count != newItems.Count)
            {
                throw new ReplaceNeededException("集合项数与 XAML 不一致");
            }

            var oldObjects = Enumerable.Range(0, oldList.Count).Select(oldList.GetAt).ToList();
            var newObjects = Enumerable.Range(0, newList.Count).Select(newList.GetAt).ToList();

            var matches = Lcs(oldItems.Select(ItemKey).ToList(), newItems.Select(ItemKey).ToList());
            var newToOld = matches.ToDictionary(m => m.Value, m => m.Key);

            var desired = new List<object>();
            var patches = new List<int>();
            for (int n = 0; n < newItems.Count; n++)
            {
                if (newToOld.TryGetValue(n, out int o))
                {
                    if (IsDeepEqual(oldItems[o], newItems[n]))
                    {
                        desired.Add(oldObjects[o]);
                        continue;
                    }

                    if (newItems[n] is XElement element && !IsOpaque(element)
                        && oldObjects[o] != null && IsTypeMatch(element, oldObjects[o]))
                    {
                        desired.Add(oldObjects[o]);
                        patches.Add(n);
                        continue;
                    }
                }

                desired.Add(newObjects[n]);
                if (newItems[n] is XElement added && newObjects[n] != null)
                {
                    context.NewItems.Add(new KeyValuePair<XElement, object>(added, newObjects[n]));
                }
            }

            // 新树上的项先摘下，才能插入正在运行的树。
            newList.Clear();

            int index = 0;
            foreach (object target in desired)
            {
                while (index < oldList.Count && !SameItem(oldList.GetAt(index), target)
                    && IndexOf(oldList, target, index) >= 0)
                {
                    oldList.RemoveAt(index);
                }

                if (index < oldList.Count && SameItem(oldList.GetAt(index), target))
                {
                    index++;
                    continue;
                }

                oldList.Insert(index, target);
                index++;
            }

            while (oldList.Count > desired.Count)
            {
                oldList.RemoveAt(oldList.Count - 1);
            }

            foreach (int n in patches)
            {
                int o = newToOld[n];
                Patch((XElement)oldItems[o], (XElement)newItems[n], oldObjects[o], newObjects[n], context);
            }
        }

        private static bool SameItem(object a, object b)
        {
            return ReferenceEquals(a, b) || (a != null && !(a is DependencyObject) && a.Equals(b));
        }

        private static int IndexOf(ListAdapter list, object item, int start)
        {
            for (int i = start; i < list.Count; i++)
            {
                if (SameItem(list.GetAt(i), item))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int IndexOf(List<object> list, object item)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (SameItem(list[i], item))
                {
                    return i;
                }
            }

            return -1;
        }

        private static List<KeyValuePair<int, int>> Lcs(List<string> a, List<string> b)
        {
            var table = new int[a.Count + 1, b.Count + 1];
            for (int i = a.Count - 1; i >= 0; i--)
            {
                for (int j = b.Count - 1; j >= 0; j--)
                {
                    table[i, j] = a[i] == b[j]
                        ? table[i + 1, j + 1] + 1
                        : Math.Max(table[i + 1, j], table[i, j + 1]);
                }
            }

            var result = new List<KeyValuePair<int, int>>();
            int x = 0, y = 0;
            while (x < a.Count && y < b.Count)
            {
                if (a[x] == b[y])
                {
                    result.Add(new KeyValuePair<int, int>(x++, y++));
                }
                else if (table[x + 1, y] >= table[x, y + 1])
                {
                    x++;
                }
                else
                {
                    y++;
                }
            }

            return result;
        }

        #endregion

        #region 属性复制

        private static bool ApplyAttribute(object target, object source, string attributeName, bool present)
        {
            Type ownerType = target.GetType();
            string propertyName = attributeName;
            int dot = attributeName.LastIndexOf('.');
            if (dot > 0)
            {
                ownerType = ResolveAttachedOwner(attributeName.Substring(0, dot));
                propertyName = attributeName.Substring(dot + 1);
                if (ownerType == null)
                {
                    return false;
                }
            }

            DependencyProperty dp = FindDependencyProperty(ownerType, propertyName);
            if (dp != null && target is DependencyObject targetObject && source is DependencyObject sourceObject)
            {
                try
                {
                    if (!present)
                    {
                        targetObject.ClearValue(dp);
                        return true;
                    }

                    object local = sourceObject.ReadLocalValue(dp);
                    if (local is BindingExpression expression)
                    {
                        BindingOperations.SetBinding(targetObject, dp, expression.ParentBinding);
                        return true;
                    }

                    object value = local == DependencyProperty.UnsetValue || !(local == null || IsPlainValue(local))
                        ? sourceObject.GetValue(dp)
                        : local;
                    targetObject.SetValue(dp, value);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(LogPrefix + $"设置 {attributeName} 失败：{ex.Message}");
                    return false;
                }
            }

            if (dot > 0)
            {
                return false;
            }

            PropertyInfo property = FindProperty(target.GetType(), propertyName);
            if (property == null || !property.CanWrite || !property.CanRead)
            {
                return false;
            }

            try
            {
                if (present)
                {
                    property.SetValue(target, property.GetValue(source));
                }
                else
                {
                    object fresh = Activator.CreateInstance(target.GetType());
                    property.SetValue(target, property.GetValue(fresh));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPlainValue(object value)
        {
            Type type = value.GetType();
            return type.IsPrimitive || type.IsEnum || type.IsValueType || value is string
                || value is DependencyObject || type.Namespace?.StartsWith("Windows.", StringComparison.Ordinal) == true;
        }

        private static readonly Dictionary<string, DependencyProperty> DependencyPropertyCache = new Dictionary<string, DependencyProperty>();

        private static DependencyProperty FindDependencyProperty(Type type, string name)
        {
            string key = type.FullName + "|" + name;
            if (DependencyPropertyCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            DependencyProperty result = null;
            for (Type t = type; t != null && result == null; t = t.BaseType)
            {
                PropertyInfo staticProperty = t.GetProperty(name + "Property",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (staticProperty != null && typeof(DependencyProperty).IsAssignableFrom(staticProperty.PropertyType))
                {
                    result = (DependencyProperty)staticProperty.GetValue(null);
                }
            }

            DependencyPropertyCache[key] = result;
            return result;
        }

        private static Dictionary<string, List<Type>> xamlTypesByName;

        private static Type ResolveAttachedOwner(string typeName)
        {
            if (xamlTypesByName == null)
            {
                IEnumerable<Type> types;
                try
                {
                    types = typeof(DependencyObject).Assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null);
                }

                xamlTypesByName = types
                    .Where(t => t.IsPublic && t.Namespace?.StartsWith("Windows.UI.Xaml", StringComparison.Ordinal) == true)
                    .GroupBy(t => t.Name)
                    .ToDictionary(g => g.Key, g => g.ToList());
            }

            if (xamlTypesByName.TryGetValue(typeName, out var candidates))
            {
                return candidates.OrderBy(t => t.Namespace == "Windows.UI.Xaml.Controls" ? 0 : 1).First();
            }

            return typeof(XamlHotReload).Assembly.GetTypes().FirstOrDefault(t => t.Name == typeName);
        }

        #endregion

        #region XAML 结构与对象树映射

        private static readonly HashSet<string> OpaqueElements = new HashSet<string>
        {
            "ResourceDictionary", "Style", "ControlTemplate", "DataTemplate", "ItemsPanelTemplate",
            "VisualStateGroup", "VisualState", "VisualTransition", "Storyboard", "Setter",
        };

        private static readonly HashSet<string> OpaqueProperties = new HashSet<string>
        {
            "Resources", "Template", "ItemTemplate", "ContentTemplate", "ItemsPanel", "Style",
            "ItemContainerStyle", "HeaderTemplate", "ItemTemplateSelector", "Transitions",
            "ContentTransitions", "ChildrenTransitions", "ItemContainerTransitions", "Triggers",
        };

        private static readonly Dictionary<string, string> ContentPropertyFallback = new Dictionary<string, string>
        {
            ["Panel"] = "Children",
            ["ItemsControl"] = "Items",
            ["ContentControl"] = "Content",
            ["UserControl"] = "Content",
            ["Border"] = "Child",
            ["Viewbox"] = "Child",
            ["Popup"] = "Child",
            ["TextBlock"] = "Inlines",
            ["RichTextBlock"] = "Blocks",
            ["Paragraph"] = "Inlines",
            ["Span"] = "Inlines",
            ["Run"] = "Text",
            ["Flyout"] = "Content",
            ["MenuFlyout"] = "Items",
            ["MenuBarItemFlyout"] = "Items",
            ["MenuFlyoutSubItem"] = "Items",
            ["MenuBar"] = "Items",
            ["MenuBarItem"] = "Items",
            ["CommandBar"] = "PrimaryCommands",
            ["NavigationView"] = "MenuItems",
            ["SwipeItems"] = "Items",
        };

        private static readonly Dictionary<Type, string> ContentPropertyCache = new Dictionary<Type, string>();

        private static string GetContentPropertyName(Type type)
        {
            if (ContentPropertyCache.TryGetValue(type, out string cached))
            {
                return cached;
            }

            string result = null;
            for (Type t = type; t != null && result == null; t = t.BaseType)
            {
                foreach (CustomAttributeData data in t.GetCustomAttributesData())
                {
                    if (data.AttributeType.Name != "ContentPropertyAttribute")
                    {
                        continue;
                    }

                    result = data.NamedArguments.Select(a => a.TypedValue.Value as string).FirstOrDefault(v => v != null)
                        ?? data.ConstructorArguments.Select(a => a.Value as string).FirstOrDefault(v => v != null);
                    if (result != null)
                    {
                        break;
                    }
                }

                if (result == null && ContentPropertyFallback.TryGetValue(t.Name, out string fallback))
                {
                    result = fallback;
                }
            }

            ContentPropertyCache[type] = result;
            return result;
        }

        private static string ResolveSlotProperty(object owner, string key)
        {
            if (key.Length == 0)
            {
                return GetContentPropertyName(owner.GetType())
                    ?? throw new ReplaceNeededException(owner.GetType().Name + " 没有内容属性");
            }

            return key.Substring(key.LastIndexOf('.') + 1);
        }

        private static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                PropertyInfo property = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    return property;
                }
            }

            return null;
        }

        private static bool IsOpaque(XElement element)
        {
            return element.Name.NamespaceName == XamlNamespace
                || OpaqueElements.Contains(element.Name.LocalName)
                || element.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value));
        }

        private static bool IsTypeMatch(XElement element, object value)
        {
            for (Type t = value.GetType(); t != null; t = t.BaseType)
            {
                if (t.Name == element.Name.LocalName)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPropertyElement(XElement element)
        {
            return element.Name.LocalName.IndexOf('.') > 0;
        }

        private static bool IsSignificant(XNode node)
        {
            return node is XElement || (node is XText text && !string.IsNullOrWhiteSpace(text.Value));
        }

        /// <summary>
        /// 把子节点按“属性槽”分组：属性元素（如 Grid.RowDefinitions）各成一组，
        /// 其余直接子节点归入内容属性（键为空字符串）。
        /// </summary>
        private static Dictionary<string, List<XNode>> SplitSlots(XElement element)
        {
            var slots = new Dictionary<string, List<XNode>>();
            foreach (XNode node in element.Nodes().Where(IsSignificant))
            {
                if (node is XElement child && IsPropertyElement(child))
                {
                    slots[child.Name.LocalName] = child.Nodes().Where(IsSignificant).ToList();
                }
                else
                {
                    if (!slots.TryGetValue(string.Empty, out var content))
                    {
                        slots[string.Empty] = content = new List<XNode>();
                    }

                    content.Add(node);
                }
            }

            return slots;
        }

        private static List<XNode> GetSlot(Dictionary<string, List<XNode>> slots, string key)
        {
            return slots.TryGetValue(key, out var items) ? items : new List<XNode>();
        }

        private static string ItemKey(XNode node)
        {
            if (node is XElement element)
            {
                var directives = element.Attributes()
                    .Where(a => a.Name.NamespaceName == XamlNamespace && a.Name.LocalName != "FieldModifier")
                    .OrderBy(a => a.Name.LocalName)
                    .Select(a => a.Name.LocalName + "=" + a.Value);
                return "E|" + element.Name + "|" + string.Join("|", directives);
            }

            return "T|" + ((XText)node).Value.Trim();
        }

        private static IEnumerable<XAttribute> PatchableAttributes(XElement element)
        {
            return element.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.NamespaceName.Length == 0);
        }

        private static bool IsDeepEqual(XNode a, XNode b)
        {
            if (a is XText ta && b is XText tb)
            {
                return ta.Value.Trim() == tb.Value.Trim();
            }

            if (!(a is XElement ea) || !(b is XElement eb) || ea.Name != eb.Name)
            {
                return false;
            }

            var attrsA = ea.Attributes().Where(x => !x.IsNamespaceDeclaration).OrderBy(x => x.Name.ToString())
                .Select(x => x.Name + "=" + x.Value);
            var attrsB = eb.Attributes().Where(x => !x.IsNamespaceDeclaration).OrderBy(x => x.Name.ToString())
                .Select(x => x.Name + "=" + x.Value);
            if (!attrsA.SequenceEqual(attrsB))
            {
                return false;
            }

            return SequenceDeepEqual(ea.Nodes().Where(IsSignificant).ToList(), eb.Nodes().Where(IsSignificant).ToList());
        }

        private static bool SequenceDeepEqual(List<XNode> a, List<XNode> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!IsDeepEqual(a[i], b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 沿 XAML 结构同步遍历运行中的对象树。集合项数对不上的槽直接跳过。
        /// </summary>
        private static void WalkPairs(XElement xml, object obj, Action<XElement, object> visit)
        {
            if (obj == null)
            {
                return;
            }

            visit(xml, obj);
            if (IsOpaque(xml))
            {
                return;
            }

            foreach (var slot in SplitSlots(xml))
            {
                List<object> values;
                try
                {
                    values = ResolveSlotValues(obj, slot.Key, slot.Value.Count);
                }
                catch
                {
                    continue;
                }

                if (values == null)
                {
                    continue;
                }

                for (int i = 0; i < slot.Value.Count; i++)
                {
                    if (slot.Value[i] is XElement child && values[i] != null && !(values[i] is string))
                    {
                        WalkPairs(child, values[i], visit);
                    }
                }
            }
        }

        private static List<object> ResolveSlotValues(object owner, string key, int expectedCount)
        {
            string property = ResolveSlotProperty(owner, key);
            if (OpaqueProperties.Contains(property))
            {
                return null;
            }

            PropertyInfo info = FindProperty(owner.GetType(), property);
            if (info == null)
            {
                return null;
            }

            object value = info.GetValue(owner);
            ListAdapter list = ListAdapter.Create(value, info.PropertyType);
            if (list != null)
            {
                return list.Count == expectedCount
                    ? Enumerable.Range(0, list.Count).Select(list.GetAt).ToList()
                    : null;
            }

            return expectedCount == 1 ? new List<object> { value } : null;
        }

        private static void AttachEvents(XElement xml, object obj, PatchContext context)
        {
            WalkPairs(xml, obj, (element, target) =>
            {
                foreach (XAttribute attribute in PatchableAttributes(element))
                {
                    if (target.GetType().GetEvent(attribute.Name.LocalName) == null)
                    {
                        continue;
                    }

                    MethodInfo method = FindHandler(context.PageType, attribute.Value);
                    if (method != null)
                    {
                        try
                        {
                            XamlPageLoader.AttachEventHandler(context.Page, target, attribute.Name.LocalName, method);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(LogPrefix + $"挂接事件 {attribute.Name.LocalName} 失败：{ex.Message}");
                        }
                    }
                }
            });
        }

        #endregion

        /// <summary>
        /// 统一访问非泛型 IList 与 WinRT 投影出的 IList&lt;T&gt;。
        /// </summary>
        private sealed class ListAdapter
        {
            private readonly IList list;
            private readonly object target;
            private readonly PropertyInfo countProperty;
            private readonly PropertyInfo itemProperty;
            private readonly MethodInfo insertMethod;
            private readonly MethodInfo removeAtMethod;
            private readonly MethodInfo clearMethod;

            private ListAdapter(IList list)
            {
                this.list = list;
            }

            private ListAdapter(object target, Type listInterface)
            {
                this.target = target;
                Type collection = typeof(ICollection<>).MakeGenericType(listInterface.GetGenericArguments());
                countProperty = collection.GetProperty("Count");
                clearMethod = collection.GetMethod("Clear");
                itemProperty = listInterface.GetProperty("Item");
                insertMethod = listInterface.GetMethod("Insert");
                removeAtMethod = listInterface.GetMethod("RemoveAt");
            }

            public static ListAdapter Create(object value, Type declaredType)
            {
                if (value == null || value is string || declaredType == typeof(object))
                {
                    return null;
                }

                if (value is IList list)
                {
                    return new ListAdapter(list);
                }

                Type generic = FindGenericList(declaredType) ?? FindGenericList(value.GetType());
                return generic != null ? new ListAdapter(value, generic) : null;
            }

            private static Type FindGenericList(Type type)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>))
                {
                    return type;
                }

                return type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));
            }

            public int Count => list?.Count ?? (int)countProperty.GetValue(target);

            public object GetAt(int index) => list != null ? list[index] : itemProperty.GetValue(target, new object[] { index });

            public void Insert(int index, object value)
            {
                if (list != null) list.Insert(index, value);
                else insertMethod.Invoke(target, new[] { index, value });
            }

            public void RemoveAt(int index)
            {
                if (list != null) list.RemoveAt(index);
                else removeAtMethod.Invoke(target, new object[] { index });
            }

            public void Clear()
            {
                if (list != null) list.Clear();
                else clearMethod.Invoke(target, null);
            }
        }
    }
}
