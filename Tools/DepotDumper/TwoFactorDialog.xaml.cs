#nullable disable
using System.Windows;
using System.Windows.Input;

namespace SolusManifestApp.Tools.DepotDumper
{
    public partial class TwoFactorDialog : Window
    {
        public string Code { get; private set; } = string.Empty;
        public bool WasCancelled { get; private set; } = false;

        public TwoFactorDialog(string prompt)
        {
            InitializeComponent();
            PromptText.Text = prompt;

            Loaded += (s, e) => CodeTextBox.Focus();
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            Code = CodeTextBox.Text.Trim();

            if (string.IsNullOrEmpty(Code))
            {
                MessageBox.Show("Please enter a code.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            WasCancelled = false;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            WasCancelled = true;
            DialogResult = false;
            Close();
        }

        private void CodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SubmitButton_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                CancelButton_Click(sender, e);
            }
        }
    }
}
