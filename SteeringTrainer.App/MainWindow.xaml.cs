using SteeringTrainer.App.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace SteeringTrainer.App
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;
            Closed += (_, _) => _vm.Dispose();

            // キーボードショートカット
            KeyDown += OnKeyDown;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // テキスト入力中はショートカット無効
            if (e.OriginalSource is System.Windows.Controls.TextBox) return;

            switch (e.Key)
            {
                case Key.Space:
                    _vm.ToggleStartStop();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    _vm.StopSession();
                    e.Handled = true;
                    break;
                case Key.F1:
                    _vm.SelectedTabIndex = 0;
                    e.Handled = true;
                    break;
                case Key.F2:
                    _vm.SelectedTabIndex = 1;
                    e.Handled = true;
                    break;
                case Key.F3:
                    _vm.SelectedTabIndex = 2;
                    e.Handled = true;
                    break;
                case Key.F4:
                    _vm.SelectedTabIndex = 3;
                    e.Handled = true;
                    break;
                case Key.F5:
                    _vm.SelectedTabIndex = 4;
                    e.Handled = true;
                    break;
            }
        }
    }
}
