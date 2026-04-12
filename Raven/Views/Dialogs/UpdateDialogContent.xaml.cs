using Microsoft.UI.Xaml.Controls;
using Raven.Helpers;

namespace Raven.Views.Dialogs;

public sealed partial class UpdateDialogContent : UserControl
{
    public UpdateDialogContent()
    {
        InitializeComponent();
        StartupPreferenceCheckBox.Content = "Settings_UpdaterStartupCheckbox".GetLocalized();
    }

    public TextBlock StatusMessageTextBlockControl => StatusMessageTextBlock;

    public CheckBox StartupPreferenceCheckBoxControl => StartupPreferenceCheckBox;

    public ProgressBar StatusProgressBarControl => StatusProgressBar;

    public TextBlock StatusProgressTextBlockControl => StatusProgressTextBlock;
}
