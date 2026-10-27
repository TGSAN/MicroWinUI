# Generates the partial class members normally provided by the XAML compiler.
# Runtime XAML loading and event hookup remain in XamlPageLoader.
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Output,
    [Parameter(Mandatory = $true)][string]$RelativePath
)

$ErrorActionPreference = 'Stop'

$xamlNamespace = 'http://schemas.microsoft.com/winfx/2006/xaml'
$presentationNamespace = 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'
$clrNamespacePrefix = 'using:'

function Convert-ToCSharpString([string]$Value) {
    return $Value.Replace('\', '\\').Replace('"', '\"')
}

function Test-CSharpIdentifier([string]$Value) {
    return $Value -match '^[A-Za-z_][A-Za-z0-9_]*$'
}

function Get-FullTypeName([System.Xml.XmlElement]$Element) {
    $localName = $Element.LocalName
    $namespaceUri = $Element.NamespaceURI

    if ($namespaceUri -eq $presentationNamespace) {
        $controls = @{
            'Page' = 'Windows.UI.Xaml.Controls.Page'
            'UserControl' = 'Windows.UI.Xaml.Controls.UserControl'
            'ContentControl' = 'Windows.UI.Xaml.Controls.ContentControl'
            'ContentPresenter' = 'Windows.UI.Xaml.Controls.ContentPresenter'
            'Grid' = 'Windows.UI.Xaml.Controls.Grid'
            'StackPanel' = 'Windows.UI.Xaml.Controls.StackPanel'
            'Border' = 'Windows.UI.Xaml.Controls.Border'
            'Canvas' = 'Windows.UI.Xaml.Controls.Canvas'
            'ScrollViewer' = 'Windows.UI.Xaml.Controls.ScrollViewer'
            'Button' = 'Windows.UI.Xaml.Controls.Button'
            'RepeatButton' = 'Windows.UI.Xaml.Controls.RepeatButton'
            'SplitButton' = 'Windows.UI.Xaml.Controls.SplitButton'
            'ToggleButton' = 'Windows.UI.Xaml.Controls.Primitives.ToggleButton'
            'ToggleSwitch' = 'Windows.UI.Xaml.Controls.ToggleSwitch'
            'CheckBox' = 'Windows.UI.Xaml.Controls.CheckBox'
            'RadioButton' = 'Windows.UI.Xaml.Controls.RadioButton'
            'Slider' = 'Windows.UI.Xaml.Controls.Slider'
            'ProgressBar' = 'Windows.UI.Xaml.Controls.ProgressBar'
            'TextBlock' = 'Windows.UI.Xaml.Controls.TextBlock'
            'TextBox' = 'Windows.UI.Xaml.Controls.TextBox'
            'PasswordBox' = 'Windows.UI.Xaml.Controls.PasswordBox'
            'AutoSuggestBox' = 'Windows.UI.Xaml.Controls.AutoSuggestBox'
            'RichEditBox' = 'Windows.UI.Xaml.Controls.RichEditBox'
            'ComboBox' = 'Windows.UI.Xaml.Controls.ComboBox'
            'ListView' = 'Windows.UI.Xaml.Controls.ListView'
            'GridView' = 'Windows.UI.Xaml.Controls.GridView'
            'ItemsControl' = 'Windows.UI.Xaml.Controls.ItemsControl'
            'FlipView' = 'Windows.UI.Xaml.Controls.FlipView'
            'Pivot' = 'Windows.UI.Xaml.Controls.Pivot'
            'PivotItem' = 'Windows.UI.Xaml.Controls.PivotItem'
            'NavigationView' = 'Windows.UI.Xaml.Controls.NavigationView'
            'NavigationViewItem' = 'Windows.UI.Xaml.Controls.NavigationViewItem'
            'NavigationViewItemHeader' = 'Windows.UI.Xaml.Controls.NavigationViewItemHeader'
            'NavigationViewItemSeparator' = 'Windows.UI.Xaml.Controls.NavigationViewItemSeparator'
            'InkToolbar' = 'Windows.UI.Xaml.Controls.InkToolbar'
            'InkCanvas' = 'Windows.UI.Xaml.Controls.InkCanvas'
            'MediaPlayerElement' = 'Windows.UI.Xaml.Controls.MediaPlayerElement'
            'CalendarView' = 'Windows.UI.Xaml.Controls.CalendarView'
            'CalendarDatePicker' = 'Windows.UI.Xaml.Controls.CalendarDatePicker'
            'DatePicker' = 'Windows.UI.Xaml.Controls.DatePicker'
            'TimePicker' = 'Windows.UI.Xaml.Controls.TimePicker'
            'ColorPicker' = 'Windows.UI.Xaml.Controls.ColorPicker'
            'RatingControl' = 'Windows.UI.Xaml.Controls.RatingControl'
            'PersonPicture' = 'Windows.UI.Xaml.Controls.PersonPicture'
            'MenuBar' = 'Windows.UI.Xaml.Controls.MenuBar'
            'MenuBarItem' = 'Windows.UI.Xaml.Controls.MenuBarItem'
            'CommandBar' = 'Windows.UI.Xaml.Controls.CommandBar'
            'AppBarButton' = 'Windows.UI.Xaml.Controls.AppBarButton'
            'AppBarToggleButton' = 'Windows.UI.Xaml.Controls.AppBarToggleButton'
            'HyperlinkButton' = 'Windows.UI.Xaml.Controls.HyperlinkButton'
            'Flyout' = 'Windows.UI.Xaml.Controls.Flyout'
            'MenuFlyout' = 'Windows.UI.Xaml.Controls.MenuFlyout'
            'MenuFlyoutItem' = 'Windows.UI.Xaml.Controls.MenuFlyoutItem'
            'MenuFlyoutSeparator' = 'Windows.UI.Xaml.Controls.MenuFlyoutSeparator'
            'MenuFlyoutSubItem' = 'Windows.UI.Xaml.Controls.MenuFlyoutSubItem'
            'TimePickerFlyout' = 'Windows.UI.Xaml.Controls.TimePickerFlyout'
            'DatePickerFlyout' = 'Windows.UI.Xaml.Controls.DatePickerFlyout'
            'KeyboardAccelerator' = 'Windows.UI.Xaml.Input.KeyboardAccelerator'
            'SolidColorBrush' = 'Windows.UI.Xaml.Media.SolidColorBrush'
            'AcrylicBrush' = 'Windows.UI.Xaml.Media.AcrylicBrush'
            'Storyboard' = 'Windows.UI.Xaml.Media.Animation.Storyboard'
            'ScaleTransform' = 'Windows.UI.Xaml.Media.ScaleTransform'
            'TranslateTransform' = 'Windows.UI.Xaml.Media.TranslateTransform'
            'TransformGroup' = 'Windows.UI.Xaml.Media.TransformGroup'
        }

        if ($controls.ContainsKey($localName)) {
            return $controls[$localName]
        }

        Write-Warning "XAML element '$localName' in '$Source' has no known generated field type; using DependencyObject."
        return 'Windows.UI.Xaml.DependencyObject'
    }

    if ($namespaceUri -eq $xamlNamespace) {
        return 'Windows.UI.Xaml.DependencyObject'
    }

    $prefix = $Element.Prefix
    if ($prefix) {
        $namespaceDeclaration = $Element.OwnerDocument.DocumentElement.GetAttribute("xmlns:$prefix")
        if ($namespaceDeclaration.StartsWith($clrNamespacePrefix, [System.StringComparison]::Ordinal)) {
            return $namespaceDeclaration.Substring($clrNamespacePrefix.Length) + '.' + $localName
        }
    }

    Write-Warning "XAML element '$localName' in '$Source' has no supported namespace; using DependencyObject."
    return 'Windows.UI.Xaml.DependencyObject'
}

function Get-FieldModifier([System.Xml.XmlElement]$Element) {
    $modifier = $Element.GetAttribute('FieldModifier', $xamlNamespace)
    if ([string]::IsNullOrWhiteSpace($modifier)) {
        return 'private'
    }

    switch ($modifier.ToLowerInvariant()) {
        'public' { return 'public' }
        'internal' { return 'internal' }
        'protected' { return 'protected' }
        'private' { return 'private' }
        default { throw "Invalid x:FieldModifier '$modifier' on '$($Element.LocalName)' in '$Source'." }
    }
}

function Add-NamedFields([System.Xml.XmlElement]$Element, [bool]$InsideTemplate, [System.Collections.Generic.List[object]]$Fields, [System.Collections.Generic.HashSet[string]]$Names) {
    $localName = $Element.LocalName
    $isTemplateBoundary = $localName -in @('DataTemplate', 'ControlTemplate', 'ItemsPanelTemplate', 'HierarchicalDataTemplate', 'Style', 'ResourceDictionary')
    $insideCurrentTemplate = $InsideTemplate -or $isTemplateBoundary

    $name = $Element.GetAttribute('Name', $xamlNamespace)
    if (-not $insideCurrentTemplate -and -not [string]::IsNullOrWhiteSpace($name)) {
        if (-not (Test-CSharpIdentifier $name)) {
            throw "x:Name '$name' in '$Source' is not a valid C# identifier."
        }
        if (-not $Names.Add($name)) {
            throw "Duplicate x:Name '$name' in '$Source'."
        }
        $Fields.Add([PSCustomObject]@{
            Name = $name
            Type = Get-FullTypeName $Element
            Modifier = Get-FieldModifier $Element
        })
    }

    foreach ($child in $Element.ChildNodes) {
        if ($child -is [System.Xml.XmlElement]) {
            Add-NamedFields $child $insideCurrentTemplate $Fields $Names
        }
    }
}

$document = New-Object System.Xml.XmlDocument
$document.PreserveWhitespace = $true
$document.Load($Source)
$root = $document.DocumentElement
$className = $root.GetAttribute('Class', $xamlNamespace)
if ([string]::IsNullOrWhiteSpace($className)) {
    Write-Warning "Skipping XAML '$Source' because it has no x:Class."
    exit 0
}

$lastDot = $className.LastIndexOf('.')
if ($lastDot -lt 1 -or $lastDot -ge ($className.Length - 1)) {
    throw "x:Class '$className' in '$Source' must contain a namespace and type name."
}

$namespaceName = $className.Substring(0, $lastDot)
$typeName = $className.Substring($lastDot + 1)
if (-not (Test-CSharpIdentifier $typeName)) {
    throw "x:Class '$className' in '$Source' contains an invalid type name."
}

$fields = New-Object 'System.Collections.Generic.List[object]'
$names = New-Object 'System.Collections.Generic.HashSet[string]'
Add-NamedFields $root $false $fields $names

$relativeXaml = Convert-ToCSharpString $RelativePath.Replace('/', '\')
$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine('// <auto-generated />')
[void]$builder.AppendLine('#pragma warning disable 0169, 0649')
[void]$builder.AppendLine('namespace ' + $namespaceName)
[void]$builder.AppendLine('{')
[void]$builder.AppendLine('    public sealed partial class ' + $typeName)
[void]$builder.AppendLine('    {')
foreach ($field in $fields) {
    [void]$builder.AppendLine('        ' + $field.Modifier + ' global::' + $field.Type + ' ' + $field.Name + ';')
}
[void]$builder.AppendLine()
[void]$builder.AppendLine('        private void InitializeComponent()')
[void]$builder.AppendLine('        {')
[void]$builder.AppendLine('            global::MicroWinUI.XamlPageLoader.Initialize(this, "' + $relativeXaml + '");')
[void]$builder.AppendLine('        }')
[void]$builder.AppendLine('    }')
[void]$builder.AppendLine('}')
[void]$builder.AppendLine('#pragma warning restore 0169, 0649')

$outputDirectory = [System.IO.Path]::GetDirectoryName($Output)
if ($outputDirectory -and -not (Test-Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$content = $builder.ToString()
if (-not (Test-Path $Output) -or [System.IO.File]::ReadAllText($Output) -ne $content) {
    [System.IO.File]::WriteAllText($Output, $content, [System.Text.UTF8Encoding]::new($false))
}
