using System.Windows;
using System.Windows.Input;

namespace HHCL.Views
{
    /// <summary>简单的文本输入弹窗（新建/重命名配置等场景用）。</summary>
    public partial class TextInputWindow : Window
    {
        public string Result { get; private set; } = "";

        public TextInputWindow(string title, string hint, string defaultValue = "")
        {
            InitializeComponent();
            TitleText.Text = title;
            HintText.Text = hint;
            InputBox.Text = defaultValue;
            Loaded += (s, e) =>
            {
                InputBox.Focus();
                InputBox.SelectAll();
            };
        }

        /// <summary>弹出输入框；用户取消或留空时返回 null。</summary>
        public static string Ask(Window owner, string title, string hint, string defaultValue = "")
        {
            var win = new TextInputWindow(title, hint, defaultValue);
            if (owner != null && owner.IsLoaded) win.Owner = owner;
            return win.ShowDialog() == true ? win.Result : null;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var v = (InputBox.Text ?? "").Trim();
            if (v.Length == 0) { InputBox.Focus(); return; }
            Result = v;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
            else if (e.Key == Key.Enter) { Ok_Click(this, new RoutedEventArgs()); e.Handled = true; }
        }
    }
}
