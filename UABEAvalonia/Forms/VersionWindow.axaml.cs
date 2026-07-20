using AssetsTools.NET.Extra;
using Avalonia;
using Avalonia.Controls;

namespace UABEAvalonia
{
    public partial class VersionWindow : Window
    {
        public VersionWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            //generated events
            btnOk.Click += BtnYes_Click;
            btnCancel.Click += BtnNo_Click;
        }

        public VersionWindow(string ver) : this()
        {
            boxVer.Text = ver;
        }

        private async void BtnYes_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            string returnText = boxVer.Text ?? string.Empty;
            // Shipped UnityVersion throws on Tuanjie "t"; accept those via helper.
            if (!TuanjieVersion.IsTuanjie(returnText))
            {
                try
                {
                    _ = new UnityVersion(returnText);
                }
                catch
                {
                    await MessageBoxUtil.ShowDialog(this, "Error", "Invalid version string. Examples: 2019.4.1f1, 2022.3.48t3 (Tuanjie)");
                    return;
                }
            }
            Close(returnText);
        }

        private void BtnNo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close(string.Empty);
        }
    }
}
