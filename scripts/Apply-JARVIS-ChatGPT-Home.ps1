#requires -Version 5.1
param([string]$RepoRoot)
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

$candidate = $RepoRoot
if ([string]::IsNullOrWhiteSpace($candidate)) { $candidate = (Get-Location).Path }
if (-not (Test-Path -LiteralPath $candidate -PathType Container)) { throw "Repository folder missing: $candidate" }
$rootResult = & git -C $candidate rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0) { throw "Not a Git repository: $candidate" }
$root = ($rootResult | Select-Object -First 1).Trim()
Set-Location -LiteralPath $root

$mainPath = Join-Path $root 'src\Council.Desktop\MainWindow.xaml'
$codePath = Join-Path $root 'src\Council.Desktop\MainWindow.xaml.cs'
if (!(Test-Path $mainPath) -or !(Test-Path $codePath)) { throw 'MainWindow source files are missing.' }

Write-Host ''
Write-Host '=== BUILD CHATGPT-STYLE COMMAND CENTER XAML ===' -ForegroundColor Cyan

$newXaml = @'
<Window x:Class="Council.Desktop.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="JARVIS — Personal AI" Height="850" Width="1340"
        MinHeight="620" MinWidth="980" WindowStartupLocation="CenterScreen"
        Background="#212121" Foreground="#ECECEC">
  <Window.Resources>
    <Style x:Key="NavButton" TargetType="Button">
      <Setter Property="Foreground" Value="#E5E5E5"/>
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="HorizontalContentAlignment" Value="Left"/>
      <Setter Property="Padding" Value="13,11"/>
      <Setter Property="Margin" Value="0,2"/>
      <Setter Property="FontSize" Value="13"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="Root" Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
              <ContentPresenter VerticalAlignment="Center" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="Root" Property="Background" Value="#303030"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="ActionTile" TargetType="Button">
      <Setter Property="Foreground" Value="#ECECEC"/>
      <Setter Property="Background" Value="#292929"/>
      <Setter Property="BorderBrush" Value="#414141"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Padding" Value="17,15"/>
      <Setter Property="Margin" Value="5"/>
      <Setter Property="HorizontalContentAlignment" Value="Left"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="Root" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="12" Padding="{TemplateBinding Padding}">
              <ContentPresenter/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="Root" Property="Background" Value="#353535"/>
                <Setter TargetName="Root" Property="BorderBrush" Value="#5A5A5A"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="Panel" TargetType="Border">
      <Setter Property="Background" Value="#282828"/>
      <Setter Property="BorderBrush" Value="#414141"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="CornerRadius" Value="11"/>
      <Setter Property="Padding" Value="15"/>
    </Style>
    <Style x:Key="SmallText" TargetType="TextBlock">
      <Setter Property="FontSize" Value="11"/>
      <Setter Property="Foreground" Value="#ADADAD"/>
      <Setter Property="TextWrapping" Value="Wrap"/>
    </Style>
    <Style x:Key="SectionTitle" TargetType="TextBlock">
      <Setter Property="FontSize" Value="10"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Foreground" Value="#A0A0A0"/>
      <Setter Property="Margin" Value="0,0,0,10"/>
    </Style>
    <Style x:Key="Metric" TargetType="TextBlock">
      <Setter Property="FontSize" Value="20"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Foreground" Value="#E7E7E7"/>
    </Style>
  </Window.Resources>

  <Grid>
    <Grid.ColumnDefinitions><ColumnDefinition Width="250"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
    <Border Grid.Column="0" Background="#171717">
      <DockPanel Margin="14,16">
        <StackPanel DockPanel.Dock="Top">
          <DockPanel Margin="4,2,4,20">
            <Border Width="34" Height="34" CornerRadius="10" Background="#29413E">
              <TextBlock Text="◉" Foreground="#70DCCE" FontSize="21" HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <StackPanel Margin="10,0,0,0" VerticalAlignment="Center">
              <TextBlock Text="JARVIS" Foreground="#F4F4F4" FontSize="17" FontWeight="SemiBold"/>
              <TextBlock Text="PERSONAL AI" Foreground="#929292" FontSize="9"/>
            </StackPanel>
          </DockPanel>
          <Button Style="{StaticResource NavButton}" Background="#2C2C2C" Content="＋   New chat" Click="OpenConsole_Click" Margin="0,0,0,16"/>
          <TextBlock Text="WORKSPACE" Foreground="#858585" FontSize="10" FontWeight="SemiBold" Margin="12,0,0,7"/>
          <Button Style="{StaticResource NavButton}" Content="◈   AI Core" Click="OpenAiCore_Click"/>
          <Button Style="{StaticResource NavButton}" Content="◎   Agents" Click="OpenAgents_Click"/>
          <Button Style="{StaticResource NavButton}" Content="▤   Tasks" Click="OpenTasks_Click"/>
          <Button Style="{StaticResource NavButton}" Content="◷   Memory" Click="ReviewMemory_Click"/>
          <Button Style="{StaticResource NavButton}" Content="▣   Conversations" Click="OpenConversations_Click"/>
          <Button Style="{StaticResource NavButton}" Content="⌕   Knowledge Base" Click="OpenKnowledgeBase_Click"/>
          <Button Style="{StaticResource NavButton}" Content="⚙   Tools &amp; Skills" Click="OpenSkills_Click"/>
          <Button Style="{StaticResource NavButton}" Content="⌘   Workflows" Click="RunWorkflow_Click"/>
          <Button Style="{StaticResource NavButton}" Content="▦   Calendar (planned)" IsEnabled="False" ToolTip="Calendar integration is not connected yet."/>
          <Border Height="1" Background="#343434" Margin="6,18,6,15"/>
          <TextBlock Text="SYSTEM" Foreground="#858585" FontSize="10" FontWeight="SemiBold" Margin="12,0,0,7"/>
          <TextBlock Text="●  Desktop application" Foreground="#A6D9BF" FontSize="11" Margin="12,5,0,5"/>
          <TextBlock Text="Local-first assistant" Foreground="#929292" FontSize="11" Margin="12,0,0,5"/>
        </StackPanel>
        <Border DockPanel.Dock="Bottom" BorderBrush="#3C3C3C" BorderThickness="1" CornerRadius="10" Padding="12" Margin="0,18,0,0">
          <StackPanel>
            <TextBlock Text="VOICE" Foreground="#E8E8E8" FontSize="11" FontWeight="SemiBold"/>
            <TextBlock Text="Push-to-talk is available in chat." Foreground="#A0A0A0" FontSize="10" TextWrapping="Wrap" Margin="0,6,0,0"/>
            <TextBlock Text="Wake-word listening is opt-in from the JARVIS tray menu." Foreground="#858585" FontSize="10" TextWrapping="Wrap" Margin="0,5,0,0"/>
          </StackPanel>
        </Border>
      </DockPanel>
    </Border>

    <Grid Grid.Column="1" Background="#212121">
      <Grid.RowDefinitions><RowDefinition Height="62"/><RowDefinition Height="*"/></Grid.RowDefinitions>
      <Border Grid.Row="0" BorderBrush="#373737" BorderThickness="0,0,0,1" Padding="25,0">
        <Grid>
          <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <StackPanel VerticalAlignment="Center">
            <TextBlock Text="JARVIS WORKSPACE" Foreground="#F0F0F0" FontSize="13" FontWeight="SemiBold"/>
            <TextBlock Text="Your personal AI command center" Foreground="#929292" FontSize="10" Margin="0,2,0,0"/>
          </StackPanel>
          <TextBlock x:Name="ClockText" Grid.Column="1" Text="--:--:--" Foreground="#DADADA" FontSize="13" VerticalAlignment="Center" Margin="0,0,20,0"/>
          <TextBlock Grid.Column="2" Text="PRIVATE DESKTOP SESSION" Foreground="#909090" FontSize="9" VerticalAlignment="Center"/>
        </Grid>
      </Border>

      <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel MaxWidth="1050" HorizontalAlignment="Center" Margin="34,35,34,35">
          <TextBlock Text="What can I help with?" Foreground="#F2F2F2" FontSize="30" FontWeight="SemiBold"
                     TextAlignment="Center" HorizontalAlignment="Center" Margin="0,15,0,10"/>
          <TextBlock Text="Research, engineering, coding, learning and everyday work — all in one assistant."
                     Foreground="#A6A6A6" FontSize="13" TextAlignment="Center" HorizontalAlignment="Center"
                     TextWrapping="Wrap" Margin="0,0,0,25"/>
          <UniformGrid Columns="2" Rows="2" Margin="0,0,0,17">
            <Button Style="{StaticResource ActionTile}" Click="OpenKnowledgeBase_Click">
              <StackPanel>
                <TextBlock Text="⌕  Research and documents" FontSize="14" FontWeight="SemiBold"/>
                <TextBlock Text="Find information, understand papers and work with project files." Foreground="#A3A3A3" FontSize="11" TextWrapping="Wrap" Margin="0,7,0,0"/>
              </StackPanel>
            </Button>
            <Button Style="{StaticResource ActionTile}" Click="StartTask_Click">
              <StackPanel>
                <TextBlock Text="＋  Start a task" FontSize="14" FontWeight="SemiBold"/>
                <TextBlock Text="Plan a task, break it into steps and track the work." Foreground="#A3A3A3" FontSize="11" TextWrapping="Wrap" Margin="0,7,0,0"/>
              </StackPanel>
            </Button>
            <Button Style="{StaticResource ActionTile}" Click="RunWorkflow_Click">
              <StackPanel>
                <TextBlock Text="⌘  Build a workflow" FontSize="14" FontWeight="SemiBold"/>
                <TextBlock Text="Combine tools and skills into a repeatable process." Foreground="#A3A3A3" FontSize="11" TextWrapping="Wrap" Margin="0,7,0,0"/>
              </StackPanel>
            </Button>
            <Button Style="{StaticResource ActionTile}" Click="TeachPreference_Click">
              <StackPanel>
                <TextBlock Text="◇  Teach JARVIS" FontSize="14" FontWeight="SemiBold"/>
                <TextBlock Text="Save preferences and project knowledge to local memory." Foreground="#A3A3A3" FontSize="11" TextWrapping="Wrap" Margin="0,7,0,0"/>
              </StackPanel>
            </Button>
          </UniformGrid>

          <Border Background="#292929" BorderBrush="#414141" BorderThickness="1" CornerRadius="13" Padding="16" Margin="0,0,0,17">
            <Grid>
              <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
              <StackPanel VerticalAlignment="Center">
                <TextBlock Text="READY WHEN YOU ARE" Foreground="#EAEAEA" FontSize="12" FontWeight="SemiBold"/>
                <TextBlock Text="Open the full JARVIS conversation with voice, memory and assistant tools." Foreground="#A0A0A0" FontSize="11" TextWrapping="Wrap" Margin="0,5,10,0"/>
              </StackPanel>
              <Button Grid.Column="1" Content="OPEN JARVIS CHAT  →" Background="#E8E8E8" Foreground="#171717"
                      BorderThickness="0" Padding="17,11" FontSize="11" FontWeight="SemiBold"
                      VerticalAlignment="Center" Click="OpenConsole_Click"/>
            </Grid>
          </Border>

          <Grid Margin="0,0,0,15">
            <Grid.ColumnDefinitions><ColumnDefinition Width="1.05*"/><ColumnDefinition Width="1.3*"/></Grid.ColumnDefinitions>
            <Border Grid.Column="0" Background="#282828" BorderBrush="#414141" BorderThickness="1" CornerRadius="11" Padding="15" Margin="0,0,8,0">
              <StackPanel>
                <TextBlock Text="SYSTEM MONITOR" Style="{StaticResource SectionTitle}"/>
                <Grid>
                  <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
                  <Grid.RowDefinitions><RowDefinition/><RowDefinition/></Grid.RowDefinitions>
                  <StackPanel Grid.Row="0" Grid.Column="0" Margin="0,0,8,13">
                    <TextBlock Text="CPU (JARVIS)" Style="{StaticResource SmallText}"/><TextBlock x:Name="CpuMetric" Text="--" Style="{StaticResource Metric}"/>
                  </StackPanel>
                  <StackPanel Grid.Row="0" Grid.Column="1" Margin="0,0,0,13">
                    <TextBlock Text="SYSTEM RAM" Style="{StaticResource SmallText}"/><TextBlock x:Name="RamMetric" Text="--" Style="{StaticResource Metric}"/>
                  </StackPanel>
                  <StackPanel Grid.Row="1" Grid.Column="0">
                    <TextBlock Text="SYSTEM DRIVE FREE" Style="{StaticResource SmallText}"/><TextBlock x:Name="DiskMetric" Text="--" Style="{StaticResource Metric}"/>
                  </StackPanel>
                  <StackPanel Grid.Row="1" Grid.Column="1">
                    <TextBlock Text="JARVIS PROCESS" Style="{StaticResource SmallText}"/><TextBlock x:Name="ProcessMetric" Text="--" Style="{StaticResource Metric}"/>
                  </StackPanel>
                </Grid>
                <TextBlock Text="Live local telemetry · updates every 2 seconds" Style="{StaticResource SmallText}" Margin="0,12,0,0"/>
              </StackPanel>
            </Border>

            <Border Grid.Column="1" Background="#282828" BorderBrush="#414141" BorderThickness="1" CornerRadius="11" Padding="15" Margin="8,0,0,0">
              <StackPanel>
                <TextBlock Text="AI PROVIDER &amp; MODEL" Style="{StaticResource SectionTitle}"/>
                <Grid>
                  <Grid.ColumnDefinitions><ColumnDefinition Width="1.2*"/><ColumnDefinition Width="1.5*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <StackPanel Grid.Column="0" Margin="0,0,8,0">
                    <TextBlock Text="PROVIDER" Style="{StaticResource SmallText}" Margin="0,0,0,5"/>
                    <ComboBox x:Name="ProviderSelector" Height="31" SelectedValuePath="Tag" SelectionChanged="ProviderSelector_SelectionChanged"
                              Background="#343434" Foreground="#F0F0F0" BorderBrush="#555555">
                      <ComboBoxItem Content="Local Ollama" Tag="ollama"/>
                      <ComboBoxItem Content="Google Gemini" Tag="gemini"/>
                      <ComboBoxItem Content="OpenAI" Tag="openai"/>
                      <ComboBoxItem Content="OmniRoute" Tag="omniroute"/>
                      <ComboBoxItem Content="Mock / test" Tag="mock"/>
                    </ComboBox>
                  </StackPanel>
                  <StackPanel Grid.Column="1" Margin="0,0,8,0">
                    <TextBlock Text="MODEL ID" Style="{StaticResource SmallText}" Margin="0,0,0,5"/>
                    <TextBox x:Name="ProviderModelInput" Height="31" Padding="7,5" Background="#202020"
                             Foreground="#EEEEEE" BorderBrush="#555555" CaretBrush="#FFFFFF" SelectionBrush="#426E67"
                             ToolTip="Enter the exact model ID installed or enabled with the selected provider."/>
                  </StackPanel>
                  <StackPanel Grid.Column="2" VerticalAlignment="Bottom">
                    <Button Content="APPLY" Click="ApplyProviderSettings_Click" Margin="0,0,0,4"
                            Background="#3A3A3A" Foreground="#F0F0F0" BorderBrush="#595959" Padding="9,6"/>
                    <Button Content="REFRESH" Click="RefreshRuntimeStatus_Click"
                            Background="#3A3A3A" Foreground="#F0F0F0" BorderBrush="#595959" Padding="9,6"/>
                  </StackPanel>
                </Grid>
                <TextBlock x:Name="ProviderStatusText" Text="Loading provider settings..." Style="{StaticResource SmallText}" Margin="0,10,0,3"/>
                <TextBlock x:Name="RuntimeReadinessText" Text="Checking local services..." Style="{StaticResource SmallText}" Margin="0,3,0,0"/>
              </StackPanel>
            </Border>
          </Grid>

          <Grid>
            <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <Border Background="#292929" BorderBrush="#414141" BorderThickness="1" CornerRadius="10" Padding="13">
              <StackPanel>
                <TextBlock Text="MEMORY INSIGHTS" Style="{StaticResource SectionTitle}"/>
                <TextBlock x:Name="MemoryCountText" Text="Loading saved conversations..." Foreground="#E4E4E4" FontSize="16" FontWeight="SemiBold"/>
                <TextBlock Text="Your existing conversation history is stored locally on this device." Style="{StaticResource SmallText}" Margin="0,5,0,0"/>
              </StackPanel>
            </Border>
            <Button Grid.Column="1" Content="OPEN TASK QUEUE  →" Click="OpenTasks_Click" Background="#353535"
                    Foreground="#EFEFEF" BorderBrush="#555555" Padding="16,10" Margin="12,0,0,0" VerticalAlignment="Center"/>
          </Grid>
        </StackPanel>
      </ScrollViewer>
    </Grid>
  </Grid>
</Window>
'@

$null = [xml]$newXaml
$requiredNames = @('ClockText','CpuMetric','RamMetric','DiskMetric','ProcessMetric','MemoryCountText',
    'ProviderSelector','ProviderModelInput','ProviderStatusText','RuntimeReadinessText')
foreach ($name in $requiredNames) {
    if ($newXaml -notmatch ('x:Name="' + [regex]::Escape($name) + '"')) { throw "Missing control: $name" }
}
$handlers = @('OpenAiCore_Click','OpenAgents_Click','OpenTasks_Click','ReviewMemory_Click',
    'OpenConversations_Click','OpenKnowledgeBase_Click','OpenSkills_Click','RunWorkflow_Click',
    'StartTask_Click','OpenConsole_Click','TeachPreference_Click','ProviderSelector_SelectionChanged',
    'ApplyProviderSettings_Click','RefreshRuntimeStatus_Click')
$mainCode = [System.IO.File]::ReadAllText($codePath)
foreach ($handler in $handlers) {
    if ($newXaml -notmatch [regex]::Escape($handler) -or $mainCode -notmatch [regex]::Escape($handler)) {
        throw "Missing MainWindow handler: $handler"
    }
}

$backup = Join-Path $env:LOCALAPPDATA ('JARVIS\Backups\ChatGPTHome-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item $mainPath (Join-Path $backup 'MainWindow.xaml')
$encoding = New-Object System.Text.UTF8Encoding -ArgumentList $false
$temp = $mainPath + '.jarvis-ui.tmp'
[System.IO.File]::WriteAllText($temp, $newXaml, $encoding)
Move-Item -LiteralPath $temp -Destination $mainPath -Force
Write-Host "Startup Command Center updated. Original backed up to $backup" -ForegroundColor Green

Write-Host ''
Write-Host '=== VERIFY CHAT WINDOW ===' -ForegroundColor Cyan
$chatXaml = [System.IO.File]::ReadAllText((Join-Path $root 'src\Council.Desktop\ChatWindow.xaml'))
$chatCode = [System.IO.File]::ReadAllText((Join-Path $root 'src\Council.Desktop\ChatWindow.xaml.cs'))
if ($chatXaml -notmatch 'What can I help with\?' -or $chatCode -notmatch 'CreateFormattedContent' -or $chatCode -notmatch 'CreateCodeBlock') {
    throw 'The ChatGPT-style conversation window changes are not present.'
}
foreach ($methodName in @('NewChat_Click','QuickCommand_Click','Suggestion_Click')) {
    $count = [regex]::Matches($chatCode, "(?m)^\s*private\s+(?:async\s+)?void\s+" + [regex]::Escape($methodName) + "\s*\(").Count
    if ($count -ne 1) { throw "Expected one $methodName method, found $count." }
}

Write-Host ''
Write-Host '=== RELEASE DESKTOP BUILD ===' -ForegroundColor Cyan
$build = Invoke-Native 'dotnet' @('build','.\src\Council.Desktop\Council.Desktop.csproj','-c','Release','--verbosity','minimal')
if ($build -ne 0) { throw "Desktop Release build failed: $build. Backup: $backup" }

Write-Host ''
Write-Host '=== RUN ALL TEST PROJECTS ===' -ForegroundColor Cyan
$projects = @(Get-ChildItem (Join-Path $root 'tests') -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue)
$failures = 0
foreach ($project in $projects) {
    Write-Host ("Testing " + $project.Name)
    $test = Invoke-Native 'dotnet' @('test',$project.FullName,'-c','Release','--verbosity','minimal')
    if ($test -ne 0) { $failures++; Write-Host ("FAILED: " + $project.Name) -ForegroundColor Red }
    else { Write-Host ("PASSED: " + $project.Name) -ForegroundColor Green }
}
if ($projects.Count -eq 0) { Write-Host 'No test projects found.' }
if ($failures -gt 0) { throw "$failures test project(s) failed. App was not restarted." }

Write-Host ''
Write-Host '=== RESTART UPDATED JARVIS ===' -ForegroundColor Cyan
$exe = Join-Path $root 'src\Council.Desktop\bin\Release\net8.0-windows\Council.Desktop.exe'
if (-not (Test-Path $exe)) { throw "Executable not found: $exe" }
$running = @(Get-Process -Name 'Council.Desktop' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host 'JARVIS is still running. Finish active requests before replacing the old process.'
    $answer = Read-Host 'Type RESTART to close the old app and launch the updated version'
    if ($answer -ceq 'RESTART') {
        foreach ($p in $running) { Stop-Process -Id $p.Id -Force }
        Start-Sleep -Seconds 2
        Start-Process -FilePath $exe
        Write-Host 'Updated JARVIS launched.' -ForegroundColor Green
    } else {
        Write-Host 'Close JARVIS from its tray menu and launch this executable when ready:'
        Write-Host $exe
    }
} else {
    Start-Process -FilePath $exe
    Write-Host 'Updated JARVIS launched.' -ForegroundColor Green
}

Write-Host ''
Write-Host '=== FINAL GIT STATUS ===' -ForegroundColor Cyan
$oldPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& git -C $root status --short
$ErrorActionPreference = $oldPreference
Write-Host 'Both startup Command Center and conversation window now have the ChatGPT-inspired layout.' -ForegroundColor Green
