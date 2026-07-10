using System.Windows;

namespace InoutPortable.App;

public partial class CredentialsDialog : Window
{
    public bool IntegratedSecurity { get; private set; }
    public string Username { get; private set; } = "";
    public string Password { get; private set; } = "";

    public CredentialsDialog(bool integrated, string username, string password)
    {
        InitializeComponent();
        IntegratedBox.IsChecked = integrated;
        UserBox.Text = username;
        PassBox.Password = password;
        UpdateState();
        Loaded += (_, _) => { if (IntegratedBox.IsChecked != true) (UserBox.Text.Length == 0 ? UserBox : (System.Windows.Controls.Control)PassBox).Focus(); };
    }

    private void Integrated_Changed(object sender, RoutedEventArgs e) => UpdateState();

    private void UpdateState()
    {
        bool integrated = IntegratedBox.IsChecked == true;
        UserBox.IsEnabled = !integrated;
        PassBox.IsEnabled = !integrated;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        IntegratedSecurity = IntegratedBox.IsChecked == true;
        Username = UserBox.Text.Trim();
        Password = PassBox.Password;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
