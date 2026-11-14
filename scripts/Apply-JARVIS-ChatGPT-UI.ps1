#requires -Version 5.1
param(
    [string]$RepoRoot
)
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

$candidateRoot = $RepoRoot
if ([string]::IsNullOrWhiteSpace($candidateRoot)) {
    $candidateRoot = $env:JARVIS_REPO_ROOT
}
if ([string]::IsNullOrWhiteSpace($candidateRoot)) {
    $candidateRoot = (Get-Location).Path
}
if (-not (Test-Path -LiteralPath $candidateRoot -PathType Container)) {
    throw "Repository folder does not exist: $candidateRoot"
}
$rootResult = & git -C $candidateRoot rev-parse --show-toplevel 2>$null
$rootExitCode = $LASTEXITCODE
if ($rootExitCode -ne 0 -or [string]::IsNullOrWhiteSpace(($rootResult | Select-Object -First 1))) {
    throw "The supplied folder is not a Git repository: $candidateRoot"
}
$root = ($rootResult | Select-Object -First 1).Trim()
Set-Location -LiteralPath $root

$xamlPath = Join-Path $root 'src\Council.Desktop\ChatWindow.xaml'
$codePath = Join-Path $root 'src\Council.Desktop\ChatWindow.xaml.cs'
foreach ($pathToCheck in @($xamlPath, $codePath)) {
    if (-not (Test-Path -LiteralPath $pathToCheck -PathType Leaf)) {
        throw "Required chat UI file is missing: $pathToCheck"
    }
}

Write-Host ''
Write-Host '=== PREPARE CHATGPT-STYLE JARVIS CHAT ===' -ForegroundColor Cyan
$xaml = [System.IO.File]::ReadAllText($xamlPath)
$code = [System.IO.File]::ReadAllText($codePath)

if ($xaml -notmatch 'x:Name="TranscriptScroll"' -or
    $xaml -notmatch 'x:Name="MessageInput"' -or
    $code -notmatch 'private void AddMessage\(') {
    throw 'The current chat window structure differs from the expected version. No files were changed.'
}

$newXaml = @'
<Window x:Class="Council.Desktop.ChatWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="JARVIS" Height="740" Width="1220"
        MinHeight="600" MinWidth="900"
        Background="#212121" Foreground="#ECECEC"
        WindowStartupLocation="CenterOwner">
  <Window.Resources>
    <SolidColorBrush x:Key="SidebarBrush" Color="#171717"/>
    <SolidColorBrush x:Key="MainBrush" Color="#212121"/>
    <SolidColorBrush x:Key="ComposerBrush" Color="#303030"/>
    <SolidColorBrush x:Key="MainTextBrush" Color="#ECECEC"/>
    <SolidColorBrush x:Key="MutedTextBrush" Color="#A4A4A4"/>
    <SolidColorBrush x:Key="AccentBrush" Color="#19B9A6"/>
    <Style x:Key="SidebarButton" TargetType="Button">
      <Setter Property="Foreground" Value="#E8E8E8"/>
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="HorizontalContentAlignment" Value="Left"/>
      <Setter Property="Padding" Value="12,10"/>
      <Setter Property="Margin" Value="0,2"/>
      <Setter Property="FontSize" Value="13"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="Root" Background="{TemplateBinding Background}" CornerRadius="8"
                    Padding="{TemplateBinding Padding}">
              <ContentPresenter VerticalAlignment="Center" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="Root" Property="Background" Value="#292929"/>
              </Trigger>
              <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="Root" Property="Background" Value="#353535"/>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.45"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="SuggestionButton" TargetType="Button">
      <Setter Property="Foreground" Value="#E6E6E6"/>
      <Setter Property="Background" Value="#272727"/>
      <Setter Property="BorderBrush" Value="#414141"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Padding" Value="16,14"/>
      <Setter Property="Margin" Value="5"/>
      <Setter Property="HorizontalContentAlignment" Value="Left"/>
      <Setter Property="FontSize" Value="13"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="Root" Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                    CornerRadius="12" Padding="{TemplateBinding Padding}">
              <ContentPresenter/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="Root" Property="Background" Value="#333333"/>
                <Setter TargetName="Root" Property="BorderBrush" Value="#5B5B5B"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="ComposerToolButton" TargetType="Button">
      <Setter Property="Foreground" Value="#D5D5D5"/>
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Padding" Value="9,6"/>
      <Setter Property="Margin" Value="2,0"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="Cursor" Value="Hand"/>
    </Style>
    <Style x:Key="SendButton" TargetType="Button">
      <Setter Property="Foreground" Value="#101010"/>
      <Setter Property="Background" Value="#F0F0F0"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Width" Value="38"/>
      <Setter Property="Height" Value="38"/>
      <Setter Property="FontSize" Value="22"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Cursor" Value="Hand"/>
    </Style>
    <Style x:Key="MessageActionButton" TargetType="Button">
      <Setter Property="Foreground" Value="#AFAFAF"/>
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Padding" Value="7,4"/>
      <Setter Property="FontSize" Value="11"/>
      <Setter Property="Cursor" Value="Hand"/>
    </Style>
  </Window.Resources>

  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="248"/>
      <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>

    <!-- ChatGPT-style left navigation -->
    <Border Grid.Column="0" Background="{StaticResource SidebarBrush}">
      <Grid Margin="13,15">
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="*"/>
          <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <DockPanel Grid.Row="0" Margin="4,3,4,20">
          <Border Width="31" Height="31" CornerRadius="9" Background="#263E3C" VerticalAlignment="Center">
            <TextBlock Text="◉" Foreground="#62E0D0" FontSize="20"
                       HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <StackPanel Margin="10,0,0,0" VerticalAlignment="Center">
            <TextBlock Text="JARVIS" FontSize="17" FontWeight="SemiBold" Foreground="#F5F5F5"/>
            <TextBlock Text="PERSONAL AI" FontSize="9" Foreground="#999999"/>
          </StackPanel>
        </DockPanel>

        <Button Grid.Row="1" Style="{StaticResource SidebarButton}"
                Background="#292929" BorderBrush="#414141" BorderThickness="1"
                Content="＋   New chat" Click="NewChat_Click" Margin="0,0,0,18"/>

        <StackPanel Grid.Row="2">
          <TextBlock Text="WORKSPACE" Foreground="#858585" FontSize="10"
                     FontWeight="SemiBold" Margin="11,4,0,7"/>
          <Button Style="{StaticResource SidebarButton}" Content="▤   Memory"
                  Tag="/memories" Click="QuickCommand_Click"/>
          <Button Style="{StaticResource SidebarButton}" Content="◇   Skills"
                  Tag="/skills" Click="QuickCommand_Click"/>
          <Button Style="{StaticResource SidebarButton}" Content="◎   Agents"
                  Tag="/agents" Click="QuickCommand_Click"/>
          <Border Height="1" Background="#303030" Margin="5,14,5,13"/>
          <TextBlock Text="RECENT" Foreground="#858585" FontSize="10"
                     FontWeight="SemiBold" Margin="11,0,0,8"/>
          <Border Background="#242424" CornerRadius="8" Padding="11,10">
            <StackPanel>
              <TextBlock Text="Current conversation" Foreground="#D7D7D7" FontSize="12"/>
              <TextBlock Text="Stored locally by JARVIS" Foreground="#858585" FontSize="10" Margin="0,4,0,0"/>
            </StackPanel>
          </Border>
          <TextBlock Text="Your recent messages are restored when JARVIS opens."
                     Foreground="#777777" FontSize="10" TextWrapping="Wrap" Margin="11,10,8,0"/>
        </StackPanel>

        <Border Grid.Row="3" BorderBrush="#353535" BorderThickness="1" CornerRadius="10"
                Padding="11" Margin="0,12,0,0">
          <StackPanel>
            <TextBlock Text="JARVIS WORKSPACE" Foreground="#E0E0E0" FontSize="11" FontWeight="SemiBold"/>
            <TextBlock Text="Conversation memory enabled" Foreground="#919191" FontSize="10" Margin="0,5,0,0"/>
            <TextBlock Text="Local and cloud providers supported" Foreground="#777777"
                       FontSize="10" TextWrapping="Wrap" Margin="0,3,0,0"/>
          </StackPanel>
        </Border>
      </Grid>
    </Border>

    <!-- Main conversation -->
    <Grid Grid.Column="1" Background="{StaticResource MainBrush}">
      <Grid.RowDefinitions>
        <RowDefinition Height="62"/>
        <RowDefinition Height="*"/>
        <RowDefinition Height="Auto"/>
      </Grid.RowDefinitions>

      <Border Grid.Row="0" BorderBrush="#303030" BorderThickness="0,0,0,1" Padding="23,0">
        <Grid>
          <Grid.ColumnDefinitions>
            <ColumnDefinition/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="Auto"/>
          </Grid.ColumnDefinitions>
          <StackPanel VerticalAlignment="Center">
            <TextBlock Text="JARVIS" Foreground="#F2F2F2" FontSize="14" FontWeight="SemiBold"/>
            <TextBlock Text="AI assistant" Foreground="#929292" FontSize="10" Margin="0,2,0,0"/>
          </StackPanel>
          <TextBlock x:Name="ProviderStatus" Grid.Column="1" Text="Connecting..."
                     Foreground="#B0B0B0" FontSize="11" VerticalAlignment="Center" Margin="0,0,18,0"/>
          <Button x:Name="PhoneInboxButton" Grid.Column="2" Content="Phone inbox (0)"
                  Padding="11,7" Background="#292929" Foreground="#E0E0E0"
                  BorderBrush="#414141" FontSize="11" Click="PhoneInbox_Click"
                  VerticalAlignment="Center"/>
        </Grid>
      </Border>

      <ScrollViewer x:Name="TranscriptScroll" Grid.Row="1"
                    VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"
                    Background="Transparent" Padding="0">
        <Grid MaxWidth="860" HorizontalAlignment="Center" Margin="28,28,28,20">
          <StackPanel x:Name="Transcript" Visibility="Collapsed"/>
          <StackPanel x:Name="WelcomePanel" VerticalAlignment="Center"
                      HorizontalAlignment="Stretch" Margin="0,50,0,35">
            <Border Width="54" Height="54" CornerRadius="18" Background="#263E3C"
                    HorizontalAlignment="Center" Margin="0,0,0,22">
              <TextBlock Text="◉" Foreground="#68DCCF" FontSize="31"
                         HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <TextBlock Text="What can I help with?" Foreground="#F3F3F3"
                       FontSize="29" FontWeight="SemiBold" HorizontalAlignment="Center"
                       TextAlignment="Center" Margin="0,0,0,9"/>
            <TextBlock Text="Ask, create, research, learn, or work on your projects."
                       Foreground="#A2A2A2" FontSize="13" HorizontalAlignment="Center"
                       TextAlignment="Center" TextWrapping="Wrap" Margin="0,0,0,26"/>
            <UniformGrid Columns="2" Rows="2">
              <Button Style="{StaticResource SuggestionButton}" Tag="Explain a research paper in simple terms, including its method, assumptions, equations and limitations."
                      Click="Suggestion_Click">
                <StackPanel><TextBlock Text="▤  Research a paper" FontWeight="SemiBold"/><TextBlock Text="Understand papers and technical documents" Foreground="#A0A0A0" FontSize="11" Margin="0,5,0,0" TextWrapping="Wrap"/></StackPanel>
              </Button>
              <Button Style="{StaticResource SuggestionButton}" Tag="Help me solve a structural engineering problem step by step. State assumptions and show the calculations."
                      Click="Suggestion_Click">
                <StackPanel><TextBlock Text="⌂  Engineering help" FontWeight="SemiBold"/><TextBlock Text="Work through analysis and design" Foreground="#A0A0A0" FontSize="11" Margin="0,5,0,0" TextWrapping="Wrap"/></StackPanel>
              </Button>
              <Button Style="{StaticResource SuggestionButton}" Tag="Help me plan and organize my next task. Break it into practical steps and identify what needs my approval."
                      Click="Suggestion_Click">
                <StackPanel><TextBlock Text="✓  Plan a task" FontWeight="SemiBold"/><TextBlock Text="Turn a goal into an actionable plan" Foreground="#A0A0A0" FontSize="11" Margin="0,5,0,0" TextWrapping="Wrap"/></StackPanel>
              </Button>
              <Button Style="{StaticResource SuggestionButton}" Tag="Help me learn a topic interactively. Start by checking my current understanding, then teach me with examples."
                      Click="Suggestion_Click">
                <StackPanel><TextBlock Text="◇  Learn something" FontWeight="SemiBold"/><TextBlock Text="An interactive tutor for any topic" Foreground="#A0A0A0" FontSize="11" Margin="0,5,0,0" TextWrapping="Wrap"/></StackPanel>
              </Button>
            </UniformGrid>
          </StackPanel>
        </Grid>
      </ScrollViewer>

      <Grid Grid.Row="2" Margin="26,8,26,15">
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <Border Grid.Row="0" Background="{StaticResource ComposerBrush}" BorderBrush="#444444"
                BorderThickness="1" CornerRadius="22" Padding="15,10">
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition/>
              <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox x:Name="MessageInput" Grid.Column="0" MinHeight="48" MaxHeight="150"
                     Padding="4,8" FontSize="15" Foreground="#F2F2F2"
                     Background="Transparent" BorderThickness="0"
                     VerticalContentAlignment="Center" AcceptsReturn="True"
                     TextWrapping="Wrap" VerticalScrollBarVisibility="Auto"
                     CaretBrush="#FFFFFF" SelectionBrush="#386F68"
                     SelectionTextBrush="#FFFFFF"
                     ToolTip="Message JARVIS. Enter sends; Shift+Enter adds a new line."
                     KeyDown="MessageInput_KeyDown"/>
            <Button Grid.Column="1" Content="↑" Style="{StaticResource SendButton}"
                    VerticalAlignment="Bottom" Margin="10,0,0,2" Click="Send_Click"/>
          </Grid>
        </Border>
        <Grid Grid.Row="1" Margin="7,7,7,0">
          <Grid.ColumnDefinitions>
            <ColumnDefinition/>
            <ColumnDefinition Width="Auto"/>
          </Grid.ColumnDefinitions>
          <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
            <Button x:Name="VoiceButton" Content="🎙 Voice" Style="{StaticResource ComposerToolButton}"
                    Click="Voice_Click"/>
            <Button x:Name="SpeakButton" Content="Speak: Off" Style="{StaticResource ComposerToolButton}"
                    Click="Speak_Click"/>
            <Button x:Name="StopButton" Content="Stop" Style="{StaticResource ComposerToolButton}"
                    IsEnabled="False" Click="Stop_Click"/>
          </StackPanel>
          <TextBlock Grid.Column="1" Text="Enter to send · Shift+Enter for a new line"
                     Foreground="#7D7D7D" FontSize="10" VerticalAlignment="Center"/>
        </Grid>
        <TextBlock Grid.Row="2" Text="JARVIS can make mistakes. Verify important information."
                   Foreground="#777777" FontSize="10" HorizontalAlignment="Center"
                   VerticalAlignment="Center" Margin="0,5,0,0"/>
      </Grid>
    </Grid>
  </Grid>
</Window>
'@

# Update first-open and refreshed-history presentation to use the welcome screen.
$oldLoadBlock = @'
            foreach (var message in _session.GetRecentMessages())
                AddMessage(message.Role == "user" ? "YOU" : "JARVIS", message.Content, message.Role != "user");
            if (_session.MessageCount == 0)
                AddMessage("JARVIS", "Hello. I'm ready. What would you like me to work on?", true);
'@
$newLoadBlock = @'
            var recentMessages = _session.GetRecentMessages();
            WelcomePanel.Visibility = recentMessages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var message in recentMessages)
                AddMessage(message.Role == "user" ? "YOU" : "JARVIS", message.Content, message.Role != "user");
'@
if ($code.Contains($oldLoadBlock)) {
    $code = $code.Replace($oldLoadBlock, $newLoadBlock)
} elseif ($code -notmatch 'var recentMessages = _session\.GetRecentMessages\(\)') {
    throw 'Could not locate the startup transcript block. No files were written.'
}

$oldRefreshBlock = @'
        var messages = _session.GetRecentMessages();
        foreach (var message in messages)
            AddMessage(message.Role == "user" ? "YOU" : "JARVIS", message.Content, message.Role != "user");
        if (messages.Count == 0)
            AddMessage("JARVIS", "Hello. I'm ready. What would you like me to work on?", true);
'@
$newRefreshBlock = @'
        var messages = _session.GetRecentMessages();
        WelcomePanel.Visibility = messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var message in messages)
            AddMessage(message.Role == "user" ? "YOU" : "JARVIS", message.Content, message.Role != "user");
'@
if ($code.Contains($oldRefreshBlock)) {
    $code = $code.Replace($oldRefreshBlock, $newRefreshBlock)
} elseif ($code -notmatch 'WelcomePanel\.Visibility = messages\.Count == 0') {
    throw 'Could not locate the transcript refresh block. No files were written.'
}

# Enter now behaves like ChatGPT: send; Shift+Enter inserts a line break.
$oldKeyBlock = @'
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            await SendMessageAsync();
        }
'@
$newKeyBlock = @'
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            await SendMessageAsync();
        }
'@
if ($code.Contains($oldKeyBlock)) {
    $code = $code.Replace($oldKeyBlock, $newKeyBlock)
} elseif ($code -notmatch 'Keyboard\.Modifiers & ModifierKeys\.Shift') {
    throw 'Could not locate the message input keyboard handler. No files were written.'
}

# Remove a previously generated chat-helper section before regenerating it.
# This makes the script safe to rerun after a failed build.
$generatedStart = $code.IndexOf('    private async void NewChat_Click(')
if ($generatedStart -ge 0) {
    $generatedMessageStart = $code.IndexOf('    private void AddMessage(', $generatedStart)
    if ($generatedMessageStart -lt 0) {
        throw 'Found a prior generated chat section but could not locate its AddMessage method. No files were written.'
    }
    $code = $code.Substring(0, $generatedStart) + $code.Substring($generatedMessageStart)
}

# Replace only the final AddMessage method and append focused rendering helpers.
$methodStart = $code.LastIndexOf('    private void AddMessage(')
$classClose = $code.LastIndexOf('}')
if ($methodStart -lt 0 -or $classClose -lt $methodStart) {
    throw 'Could not safely locate the final AddMessage method. No files were written.'
}

$newMethods = @'
    private async void NewChat_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
            return;
        if (_sending)
        {
            ProviderStatus.Text = "Stop the current response before starting a new chat.";
            return;
        }

        await _session.ClearAsync();
        Transcript.Children.Clear();
        WelcomePanel.Visibility = Visibility.Visible;
        MessageInput.Clear();
        ProviderStatus.Text = "●  " + _session.ProviderName + " · new conversation";
        MessageInput.Focus();
    }

    private async void QuickCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string command)
            return;
        MessageInput.Text = command;
        await SendMessageAsync();
    }

    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string prompt)
        {
            MessageInput.Text = prompt;
            MessageInput.Focus();
            MessageInput.CaretIndex = MessageInput.Text.Length;
        }
    }

    private void AddMessage(string speaker, string content, bool assistant)
    {
        WelcomePanel.Visibility = Visibility.Collapsed;

        var messageRoot = new Border
        {
            MaxWidth = 860,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            Padding = new Thickness(0, 15, 0, 15),
            Background = System.Windows.Media.Brushes.Transparent
        };

        var messageStack = new StackPanel();
        if (!assistant)
        {
            var userBubble = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromRgb(48, 48, 48)),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(17, 12, 17, 12),
                MaxWidth = 690,
                HorizontalAlignment = WpfHorizontalAlignment.Right
            };
            var userText = new TextBlock
            {
                Foreground = new SolidColorBrush(WpfColor.FromRgb(239, 239, 239)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22
            };
            AddInlineMarkdown(userText, content);
            userBubble.Child = userText;
            messageStack.Children.Add(userBubble);
        }
        else
        {
            var identity = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var avatar = new Border
            {
                Width = 27,
                Height = 27,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(WpfColor.FromRgb(38, 62, 60)),
                Margin = new Thickness(0, 0, 9, 0)
            };
            avatar.Child = new TextBlock
            {
                Text = "◉",
                Foreground = new SolidColorBrush(WpfColor.FromRgb(104, 220, 207)),
                FontSize = 17,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            identity.Children.Add(avatar);
            identity.Children.Add(new TextBlock
            {
                Text = speaker,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(235, 235, 235)),
                FontSize = 12,
                FontWeight = System.Windows.FontWeights.SemiBold,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
            messageStack.Children.Add(identity);
            messageStack.Children.Add(CreateFormattedContent(content));

            var actions = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(-6, 7, 0, 0)
            };
            var copyButton = new System.Windows.Controls.Button
            {
                Content = "Copy response",
                Style = (Style)FindResource("MessageActionButton"),
                ToolTip = "Copy the complete JARVIS response"
            };
            copyButton.Click += (_, _) => CopyText(content);
            actions.Children.Add(copyButton);
            messageStack.Children.Add(actions);
        }

        messageRoot.Child = messageStack;
        Transcript.Children.Add(messageRoot);
        TranscriptScroll.Dispatcher.BeginInvoke(
            new Action(() => TranscriptScroll.ScrollToEnd()),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private StackPanel CreateFormattedContent(string content)
    {
        var panel = new StackPanel();
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var codeLines = new List<string>();
        var inCode = false;
        var codeLanguage = string.Empty;

        foreach (var originalLine in lines)
        {
            var trimmed = originalLine.Trim();
            var fence = new string((char)96, 3);
            if (trimmed.StartsWith(fence, StringComparison.Ordinal))
            {
                if (!inCode)
                {
                    inCode = true;
                    codeLanguage = trimmed.Length > 3 ? trimmed.Substring(3).Trim() : string.Empty;
                    codeLines.Clear();
                }
                else
                {
                    panel.Children.Add(CreateCodeBlock(string.Join(Environment.NewLine, codeLines), codeLanguage));
                    codeLines.Clear();
                    inCode = false;
                    codeLanguage = string.Empty;
                }
                continue;
            }

            if (inCode)
            {
                codeLines.Add(originalLine);
                continue;
            }

            if (string.IsNullOrWhiteSpace(originalLine))
                continue;

            var heading = System.Text.RegularExpressions.Regex.Match(originalLine, @"^\s{0,3}(#{1,3})\s+(.+)$");
            if (heading.Success)
            {
                var level = heading.Groups[1].Length;
                var headingText = new TextBlock
                {
                    FontSize = level == 1 ? 22 : level == 2 ? 18 : 16,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(242, 242, 242)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 13, 0, 7)
                };
                AddInlineMarkdown(headingText, heading.Groups[2].Value);
                panel.Children.Add(headingText);
                continue;
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^([-*_])\1{2,}$"))
            {
                panel.Children.Add(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(WpfColor.FromRgb(65, 65, 65)),
                    Margin = new Thickness(0, 10, 0, 13)
                });
                continue;
            }

            var listItem = System.Text.RegularExpressions.Regex.Match(originalLine, @"^\s*(?:([-*+])|(\d+)\.)\s+(.+)$");
            if (listItem.Success)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 5) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var marker = new TextBlock
                {
                    Text = listItem.Groups[2].Success ? listItem.Groups[2].Value + "." : "•",
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(190, 190, 190)),
                    FontSize = 14
                };
                var itemText = new TextBlock
                {
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(225, 225, 225)),
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 22
                };
                AddInlineMarkdown(itemText, listItem.Groups[3].Value);
                Grid.SetColumn(marker, 0);
                Grid.SetColumn(itemText, 1);
                row.Children.Add(marker);
                row.Children.Add(itemText);
                panel.Children.Add(row);
                continue;
            }

            var paragraph = new TextBlock
            {
                Foreground = new SolidColorBrush(WpfColor.FromRgb(225, 225, 225)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 8)
            };
            AddInlineMarkdown(paragraph, originalLine);
            panel.Children.Add(paragraph);
        }

        if (inCode && codeLines.Count > 0)
            panel.Children.Add(CreateCodeBlock(string.Join(Environment.NewLine, codeLines), codeLanguage));

        return panel;
    }

    private static void AddInlineMarkdown(TextBlock target, string text)
    {
        var pattern = @"(\*\*.+?\*\*|\x60.+?\x60|\*.+?\*)";
        var matches = System.Text.RegularExpressions.Regex.Matches(text, pattern);
        var cursor = 0;

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Index > cursor)
                target.Inlines.Add(new System.Windows.Documents.Run(text.Substring(cursor, match.Index - cursor)));

            var token = match.Value;
            if (token.StartsWith("**", StringComparison.Ordinal) && token.EndsWith("**", StringComparison.Ordinal))
            {
                target.Inlines.Add(new System.Windows.Documents.Run(token.Substring(2, token.Length - 4))
                {
                    FontWeight = System.Windows.FontWeights.SemiBold
                });
            }
            else if (token.Length >= 2 && token[0] == (char)96 && token[token.Length - 1] == (char)96)
            {
                target.Inlines.Add(new System.Windows.Documents.Run(token.Substring(1, token.Length - 2))
                {
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 13,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(171, 215, 205)),
                    Background = new SolidColorBrush(WpfColor.FromRgb(48, 55, 53))
                });
            }
            else if (token.StartsWith("*", StringComparison.Ordinal) && token.EndsWith("*", StringComparison.Ordinal))
            {
                target.Inlines.Add(new System.Windows.Documents.Run(token.Substring(1, token.Length - 2))
                {
                    FontStyle = System.Windows.FontStyles.Italic
                });
            }
            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
            target.Inlines.Add(new System.Windows.Documents.Run(text.Substring(cursor)));
        if (matches.Count == 0)
            target.Text = text;
    }

    private Border CreateCodeBlock(string code, string language)
    {
        var container = new Border
        {
            Background = new SolidColorBrush(WpfColor.FromRgb(25, 25, 25)),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(61, 61, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 8, 0, 13)
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var label = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(language) ? "CODE" : language.ToUpperInvariant(),
            Foreground = new SolidColorBrush(WpfColor.FromRgb(158, 158, 158)),
            FontSize = 10,
            FontWeight = System.Windows.FontWeights.SemiBold,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        DockPanel.SetDock(label, Dock.Left);
        header.Children.Add(label);

        var copyCode = new System.Windows.Controls.Button
        {
            Content = "Copy code",
            Style = (Style)FindResource("MessageActionButton"),
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };
        copyCode.Click += (_, _) => CopyText(code);
        DockPanel.SetDock(copyCode, Dock.Right);
        header.Children.Add(copyCode);

        var codeText = new System.Windows.Controls.TextBox
        {
            Text = code,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            MinHeight = 34,
            MaxHeight = 250,
            SelectionBrush = new SolidColorBrush(WpfColor.FromRgb(49, 105, 95)),
            SelectionTextBrush = System.Windows.Media.Brushes.White
        };

        Grid.SetRow(header, 0);
        Grid.SetRow(codeText, 1);
        layout.Children.Add(header);
        layout.Children.Add(codeText);
        container.Child = layout;
        return container;
    }

    private void CopyText(string text)
    {
        try
        {
            if (!string.IsNullOrEmpty(text))
                System.Windows.Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            ProviderStatus.Text = "Clipboard temporarily unavailable: " + ex.Message;
        }
    }
'@

$code = $code.Substring(0, $methodStart) + $newMethods + [Environment]::NewLine + '}'

# Fail before writing if any generated event handler appears more than once.
foreach ($methodName in @('NewChat_Click', 'QuickCommand_Click', 'Suggestion_Click')) {
    $escapedName = [regex]::Escape($methodName)
    $methodMatches = [regex]::Matches($code, "(?m)^\s*private\s+(?:async\s+)?void\s+$escapedName\s*\(")
    if ($methodMatches.Count -ne 1) {
        throw "Expected exactly one $methodName method after generation; found $($methodMatches.Count). No files were written."
    }
}

# Validate the XAML and key event/control bindings before backing up or writing.
try {
    $null = [xml]$newXaml
} catch {
    throw "New chat XAML is invalid. No files were written: $($_.Exception.Message)"
}
$requiredBindings = @(
    'TranscriptScroll','Transcript','WelcomePanel','MessageInput',
    'ProviderStatus','PhoneInboxButton','VoiceButton','SpeakButton','StopButton',
    'NewChat_Click','QuickCommand_Click','Suggestion_Click','PhoneInbox_Click',
    'Voice_Click','Speak_Click','Stop_Click','Send_Click','MessageInput_KeyDown'
)
foreach ($binding in $requiredBindings) {
    if ($newXaml -notmatch [regex]::Escape($binding)) {
        throw "New XAML is missing required binding: $binding"
    }
    if ($binding -match '_Click$|_KeyDown$' -and $code -notmatch [regex]::Escape($binding)) {
        throw "Code-behind handler is missing: $binding"
    }
}

$backup = Join-Path $env:LOCALAPPDATA ('JARVIS\Backups\ChatGPTUI-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item $xamlPath (Join-Path $backup 'ChatWindow.xaml')
Copy-Item $codePath (Join-Path $backup 'ChatWindow.xaml.cs')

$encoding = New-Object System.Text.UTF8Encoding -ArgumentList $false
$xamlTemp = $xamlPath + '.jarvis-chat.tmp'
$codeTemp = $codePath + '.jarvis-chat.tmp'
[System.IO.File]::WriteAllText($xamlTemp, $newXaml, $encoding)
[System.IO.File]::WriteAllText($codeTemp, $code, $encoding)
Move-Item -LiteralPath $xamlTemp -Destination $xamlPath -Force
Move-Item -LiteralPath $codeTemp -Destination $codePath -Force

Write-Host "ChatGPT-style layout applied. Original files backed up to $backup" -ForegroundColor Green

Write-Host ''
Write-Host '=== RELEASE BUILD ===' -ForegroundColor Cyan
$buildCode = Invoke-Native 'dotnet' @('build', '.\src\Council.Desktop\Council.Desktop.csproj', '-c', 'Release', '--verbosity', 'minimal')
if ($buildCode -ne 0) {
    Write-Host "Build failed (exit $buildCode). Original chat files are backed up." -ForegroundColor Red
    exit $buildCode
}

Write-Host ''
Write-Host '=== TEST ALL PROJECTS ===' -ForegroundColor Cyan
$testProjects = @(Get-ChildItem (Join-Path $root 'tests') -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue)
$failedTests = 0
foreach ($project in $testProjects) {
    Write-Host ("Running {0}" -f $project.Name)
    $testCode = Invoke-Native 'dotnet' @('test', $project.FullName, '-c', 'Release', '--verbosity', 'minimal')
    if ($testCode -ne 0) {
        $failedTests++
        Write-Host ("FAILED: {0} (exit {1})" -f $project.Name, $testCode) -ForegroundColor Red
    } else {
        Write-Host ("PASSED: {0}" -f $project.Name) -ForegroundColor Green
    }
}
if ($testProjects.Count -eq 0) { Write-Host 'No test projects found under .\tests.' }

Write-Host ''
Write-Host '=== LAUNCH JARVIS ===' -ForegroundColor Cyan
$exe = Join-Path $root 'src\Council.Desktop\bin\Release\net8.0-windows\Council.Desktop.exe'
if (Test-Path $exe) {
    $running = @(Get-Process -Name 'Council.Desktop' -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) {
        Start-Process -FilePath $exe
        Write-Host 'JARVIS desktop launched.' -ForegroundColor Green
    } else {
        Write-Host 'JARVIS desktop is already running. Close and reopen it to load the updated chat UI.'
    }
} else {
    Write-Host 'Desktop executable was not found.' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '=== FINAL GIT STATUS ===' -ForegroundColor Cyan
$old = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& git -C $root status --short
$ErrorActionPreference = $old

if ($failedTests -gt 0) {
    Write-Host "$failedTests test project(s) failed; review the output above." -ForegroundColor Yellow
    exit 2
}
Write-Host 'ChatGPT-style UI pass completed.' -ForegroundColor Green
