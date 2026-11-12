using System.ComponentModel;
using System.Windows.Controls;
using Atom.Toolbox.Desktop.ViewModels.Items;

namespace Atom.Toolbox.Desktop.Views;

public partial class ConsolePanel : UserControl
{
    private INotifyPropertyChanged? _observed;

    public ConsolePanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnSourceChanged;
        }

        _observed = e.NewValue as INotifyPropertyChanged;
        if (_observed is not null)
        {
            _observed.PropertyChanged += OnSourceChanged;
        }
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConsoleViewModel.Text))
        {
            Dispatcher.BeginInvoke(new Action(() => Scroller.ScrollToEnd()));
        }
    }
}
