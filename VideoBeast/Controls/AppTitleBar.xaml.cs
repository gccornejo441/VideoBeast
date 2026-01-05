using Microsoft.UI.Xaml.Controls;

using Windows.Foundation;

namespace VideoBeast.Controls;

public sealed partial class AppTitleBar : UserControl
{
    public AppTitleBar()
    {
        InitializeComponent();

        TopSearchBox.TextChanged += (s,e) => TextChanged?.Invoke(s,e);
        TopSearchBox.SuggestionChosen += (s,e) => SuggestionChosen?.Invoke(s,e);
        TopSearchBox.QuerySubmitted += (s,e) => QuerySubmitted?.Invoke(s,e);
        titleBar.PaneToggleRequested += (s,e) => PaneToggleRequested?.Invoke(s,e);
        titleBar.BackRequested += (s,e) => BackRequested?.Invoke(s,e);
    }

    public TitleBar TitleBarControl => titleBar;

    public AutoSuggestBox SearchBox => TopSearchBox;

    public event TypedEventHandler<AutoSuggestBox,AutoSuggestBoxTextChangedEventArgs>? TextChanged;
    public event TypedEventHandler<AutoSuggestBox,AutoSuggestBoxSuggestionChosenEventArgs>? SuggestionChosen;
    public event TypedEventHandler<AutoSuggestBox,AutoSuggestBoxQuerySubmittedEventArgs>? QuerySubmitted;

    public event TypedEventHandler<TitleBar,object>? PaneToggleRequested;
    public event TypedEventHandler<TitleBar,object>? BackRequested;
}
