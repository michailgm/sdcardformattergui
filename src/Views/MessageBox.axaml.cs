using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SDCardFormatterApp;

public enum MessageBoxButton
{
    OK,
    OKCancel,
    YesNo,
    YesNoCancel
}

public enum MessageBoxResult
{
    None,
    OK,
    Cancel,
    Yes,
    No
}

public enum MessageBoxIcon
{
    None,
    Information,
    Warning,
    Error,
    Question
}

public partial class MessageBox : Window
{
    private StackPanel buttonPanel;
    private Image iconImage;
    private TextBlock messageText;
    private MessageBoxResult result = MessageBoxResult.None;

    public MessageBox()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        messageText = this.FindControl<TextBlock>("MessageText");
        buttonPanel = this.FindControl<StackPanel>("ButtonPanel");
        iconImage = this.FindControl<Image>("IconImage");

        if (messageText == null || buttonPanel == null || iconImage == null) throw new InvalidOperationException("Controls not found");
    }

    public static async Task<MessageBoxResult> Show(Window parent, string title, string message, MessageBoxButton buttons, MessageBoxIcon icon = MessageBoxIcon.None)
    {
        MessageBox dialog = new MessageBox
        {
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        dialog.messageText.Text = message;
        dialog.SetIcon(icon);
        dialog.CreateButtons(buttons);

        await dialog.ShowDialog(parent);
        return dialog.result;
    }

    private void SetIcon(MessageBoxIcon icon)
    {
        string iconPath = icon switch
        {
            MessageBoxIcon.Information => "avares://SDCardFormatterApp/Assets/info_72pt.png",
            MessageBoxIcon.Warning => "avares://SDCardFormatterApp/Assets/warning_72pt.png",
            MessageBoxIcon.Error => "avares://SDCardFormatterApp/Assets/error_72pt.png",
            MessageBoxIcon.Question => "avares://SDCardFormatterApp/Assets/info_72pt.png",
            _ => null
        };

        if (string.IsNullOrEmpty(iconPath))
        {
            iconImage.IsVisible = false;
            return;
        }

        try
        {
            Uri uri = new Uri(iconPath);
            iconImage.Source = new Bitmap(AssetLoader.Open(uri));
            iconImage.IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load icon: {ex.Message}");
            iconImage.IsVisible = false;
        }
    }

    private void CreateButtons(MessageBoxButton buttons)
    {
        switch (buttons)
        {
            case MessageBoxButton.OK:
                AddButton("OK", MessageBoxResult.OK, true, "ok_32pt.png");
                break;

            case MessageBoxButton.OKCancel:
                AddButton("OK", MessageBoxResult.OK, true, "ok_32pt.png");
                AddButton("Cancel", MessageBoxResult.Cancel, false, "cancel_32pt.png");
                break;

            case MessageBoxButton.YesNo:
                AddButton("Yes", MessageBoxResult.Yes, true, "ok_32pt.png");
                AddButton("No", MessageBoxResult.No, false, "cancel_32pt.png");
                break;

            case MessageBoxButton.YesNoCancel:
                AddButton("Yes", MessageBoxResult.Yes, true, "ok_32pt.png");
                AddButton("No", MessageBoxResult.No, false, "cancel_32pt.png");
                AddButton("Cancel", MessageBoxResult.Cancel, false, "cancel_32pt.png");
                break;
        }

        Button defaultButton = buttonPanel.Children.OfType<Button>().FirstOrDefault(b => b.IsDefault);
        if (defaultButton == null && buttonPanel.Children.Count > 0)
            defaultButton = buttonPanel.Children[0] as Button;

        Opened += (sender, e) => { defaultButton.Focus(); };
    }

    private void AddButton(string content, MessageBoxResult result, bool isDefault, string iconFileName)
    {
        StackPanel buttonContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        try
        {
            string iconPath = $"avares://SDCardFormatterApp/Assets/{iconFileName}";
            Image icon = new Image
            {
                Source = new Bitmap(AssetLoader.Open(new Uri(iconPath))),
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 3, 0)
            };
            buttonContent.Children.Add(icon);
        }
        catch
        {
        }

        TextBlock text = new TextBlock
        {
            Text = content,
            VerticalAlignment = VerticalAlignment.Center
        };
        buttonContent.Children.Add(text);

        Button button = new Button
        {
            Content = buttonContent,
            Width = 100,
            Height = 35,
            IsDefault = isDefault,
            Padding = new Thickness(10, 5)
        };

        button.Click += (sender, e) =>
        {
            this.result = result;
            Close();
        };

        if (buttonPanel.Children.Count == 0)
        {
            button.Background = SolidColorBrush.Parse("#0078D7");
            button.Foreground = new SolidColorBrush(Colors.White);
        }

        buttonPanel.Children.Add(button);
    }
}