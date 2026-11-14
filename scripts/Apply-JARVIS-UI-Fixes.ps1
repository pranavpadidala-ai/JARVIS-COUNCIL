#requires -Version 5.1
$ErrorActionPreference = 'Stop'

function Invoke-Native {
    param([string]$File, [string[]]$Arguments)
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & $File @Arguments 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = $old
    foreach ($line in $output) { Write-Host $line }
    return $code
}

$root = (& git rev-parse --show-toplevel 2>$null | Select-Object -First 1)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
    throw 'Run this script from inside the JARVIS repository.'
}
$root = $root.Trim()
Set-Location $root

$appPath = Join-Path $root 'src\Council.Desktop\App.xaml'
$chatPath = Join-Path $root 'src\Council.Desktop\ChatWindow.xaml.cs'
$mainPath = Join-Path $root 'src\Council.Desktop\MainWindow.xaml'
foreach ($path in @($appPath, $chatPath, $mainPath)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing UI source file: $path" }
}

# Do not overwrite any UI file that has acquired local edits since the review package.
$old = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$uiStatus = & git status --short -- src/Council.Desktop/App.xaml src/Council.Desktop/ChatWindow.xaml.cs src/Council.Desktop/MainWindow.xaml 2>$null
$gitCode = $LASTEXITCODE
$ErrorActionPreference = $old
if ($gitCode -ne 0) { throw 'Could not inspect UI file status.' }
if (@($uiStatus).Count -gt 0) {
    $uiStatus | ForEach-Object { Write-Host $_ }
    throw 'UI files have local changes. Nothing was modified; review those changes first.'
}

Write-Host ''
Write-Host '=== PREPARE UI FIXES ===' -ForegroundColor Cyan
$app = [System.IO.File]::ReadAllText($appPath)
$chat = [System.IO.File]::ReadAllText($chatPath)
$main = [System.IO.File]::ReadAllText($mainPath)
$originalApp = $app
$originalChat = $chat
$originalMain = $main

# Global TextBox style gives editable fields clear text, caret and selection colors.
if ($app -notmatch '<Style\s+TargetType="TextBox"') {
    $marker = '    <Style TargetType="TextBlock">'
    if (!$app.Contains($marker)) { throw 'App.xaml style insertion marker was not found.' }
    $style = @'
    <Style TargetType="TextBox">
      <Setter Property="Foreground" Value="#E6F5FA"/>
      <Setter Property="Background" Value="#0D2233"/>
      <Setter Property="BorderBrush" Value="#315D73"/>
      <Setter Property="CaretBrush" Value="#41DDF4"/>
      <Setter Property="SelectionBrush" Value="#267B9D"/>
      <Setter Property="SelectionTextBrush" Value="#FFFFFF"/>
      <Setter Property="FontFamily" Value="Segoe UI"/>
    </Style>
'@
    $app = $app.Replace($marker, $style + [Environment]::NewLine + $marker)
}
$app = $app.Replace(
    '<Setter Property="Foreground" Value="#D7F4FF"/>',
    '<Setter Property="Foreground" Value="#E6F5FA"/>'
)

# Message bodies were TextBlocks, so users could not reliably select/copy their text.
$pattern = '(?s)        stack\.Children\.Add\(new TextBlock\s*\{\s*Text = content,\s*Foreground = new SolidColorBrush\(\(WpfColor\)WpfColorConverter\.ConvertFromString\("#D7F1FA"\)\),\s*FontSize = 13,\s*TextWrapping = TextWrapping\.Wrap\s*\}\);'
$rx = [System.Text.RegularExpressions.Regex]::new($pattern)
if ($rx.IsMatch($chat)) {
    $replacement = @'
        var messageText = new TextBox
        {
            Text = content,
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#E6F4FA")),
            SelectionBrush = new SolidColorBrush(WpfColor.FromRgb(38, 123, 157)),
            SelectionTextBrush = Brushes.White,
            CaretBrush = new SolidColorBrush(WpfColor.FromRgb(65, 221, 244)),
            FontSize = 14,
        };

        var messageMenu = new ContextMenu();
        var copyMenuItem = new MenuItem { Header = "Copy" };
        copyMenuItem.Click += (_, _) =>
        {
            try
            {
                var textToCopy = string.IsNullOrEmpty(messageText.SelectedText)
                    ? messageText.Text
                    : messageText.SelectedText;
                if (!string.IsNullOrEmpty(textToCopy))
                    Clipboard.SetText(textToCopy);
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                ProviderStatus.Text = "Clipboard temporarily unavailable: " + ex.Message;
            }
        };
        var selectAllMenuItem = new MenuItem { Header = "Select all" };
        selectAllMenuItem.Click += (_, _) =>
        {
            messageText.Focus();
            messageText.SelectAll();
        };
        messageMenu.Items.Add(copyMenuItem);
        messageMenu.Items.Add(selectAllMenuItem);
        messageText.ContextMenu = messageMenu;
        stack.Children.Add(messageText);
'@
    $chat = $rx.Replace($chat, $replacement, 1)
} elseif ($chat -notmatch 'var messageText = new TextBox') {
    throw 'Transcript renderer did not match the expected source. Nothing was modified.'
}
$chat = $chat.Replace(
    'Foreground="#9BB9C8" FontSize="11" TextWrapping="Wrap"',
    'Foreground="#BBD4E0" FontSize="12" TextWrapping="Wrap"'
)

# Slightly improve small-text contrast and size while keeping the original dashboard composition.
$main = $main.Replace(
    '<Setter Property="FontSize" Value="10"/><Setter Property="FontWeight" Value="Bold"/>',
    '<Setter Property="FontSize" Value="11"/><Setter Property="FontWeight" Value="Bold"/>'
)
$main = $main.Replace(
    '<Setter Property="Foreground" Value="#6F9EB5"/>',
    '<Setter Property="Foreground" Value="#8DB5C8"/>'
)
$main = $main.Replace(
    '<Setter Property="FontSize" Value="11"/><Setter Property="Foreground" Value="#86A9BA"/>',
    '<Setter Property="FontSize" Value="12"/><Setter Property="Foreground" Value="#A7C2D0"/>'
)

# Validate every affected XAML document before writing any modified file.
try {
    $null = [xml]$app
    $null = [xml]$main
    $null = [xml]([System.IO.File]::ReadAllText((Join-Path $root 'src\Council.Desktop\ChatWindow.xaml')))
} catch {
    throw "XAML validation failed. No UI file was written: $($_.Exception.Message)"
}

if ($app -eq $originalApp -and $chat -eq $originalChat -and $main -eq $originalMain) {
    Write-Host 'The UI fixes are already applied; no source files changed.'
} else {
    $backup = Join-Path $env:LOCALAPPDATA ('JARVIS\Backups\UIFix-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    Copy-Item $appPath (Join-Path $backup 'App.xaml')
    Copy-Item $chatPath (Join-Path $backup 'ChatWindow.xaml.cs')
    Copy-Item $mainPath (Join-Path $backup 'MainWindow.xaml')

    $encoding = New-Object System.Text.UTF8Encoding -ArgumentList $false
    $items = @(
        @{ Path = $appPath; Text = $app },
        @{ Path = $chatPath; Text = $chat },
        @{ Path = $mainPath; Text = $main }
    )
    foreach ($item in $items) {
        $temp = $item.Path + '.jarvis-ui.tmp'
        [System.IO.File]::WriteAllText($temp, $item.Text, $encoding)
        Move-Item -LiteralPath $temp -Destination $item.Path -Force
    }
    Write-Host "UI fixes applied. Original files backed up to $backup" -ForegroundColor Green
}

Write-Host ''
Write-Host '=== RELEASE DESKTOP BUILD ===' -ForegroundColor Cyan
$buildCode = Invoke-Native 'dotnet' @('build', '.\src\Council.Desktop\Council.Desktop.csproj', '-c', 'Release', '--verbosity', 'minimal')
if ($buildCode -ne 0) {
    Write-Host "Desktop build failed (exit $buildCode). The original UI files are backed up." -ForegroundColor Red
    exit $buildCode
}

Write-Host ''
Write-Host '=== TEST PROJECTS ===' -ForegroundColor Cyan
$testProjects = @(Get-ChildItem (Join-Path $root 'tests') -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue)
$failedTests = 0
if ($testProjects.Count -eq 0) {
    Write-Host 'No test projects found under .\tests.'
}
foreach ($project in $testProjects) {
    Write-Host ("Running {0}" -f $project.Name)
    $testCode = Invoke-Native 'dotnet' @('test', $project.FullName, '-c', 'Release', '--verbosity', 'minimal')
    if ($testCode -ne 0) {
        $failedTests++
        Write-Host ("FAILED: {0}, exit code {1}" -f $project.Name, $testCode) -ForegroundColor Red
    } else {
        Write-Host ("PASSED: {0}" -f $project.Name) -ForegroundColor Green
    }
}

Write-Host ''
Write-Host '=== START UPDATED DESKTOP ===' -ForegroundColor Cyan
$exe = Join-Path $root 'src\Council.Desktop\bin\Release\net8.0-windows\Council.Desktop.exe'
if (Test-Path $exe) {
    $existing = @(Get-Process -Name 'Council.Desktop' -ErrorAction SilentlyContinue)
    if ($existing.Count -eq 0) {
        Start-Process -FilePath $exe
        Write-Host 'Updated JARVIS Command Center launched.' -ForegroundColor Green
    } else {
        Write-Host 'JARVIS is already running. Close and reopen it to load the new UI.'
    }
} else {
    Write-Host 'Executable not found; skipped launch.' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '=== FINAL GIT STATUS ===' -ForegroundColor Cyan
$old = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& git status --short
$ErrorActionPreference = $old

if ($failedTests -gt 0) {
    Write-Host "$failedTests test project(s) failed. See output above." -ForegroundColor Yellow
    exit 2
}
Write-Host 'UI repair pass completed. In chat, drag-select message text or right-click and choose Copy / Select all.' -ForegroundColor Green
