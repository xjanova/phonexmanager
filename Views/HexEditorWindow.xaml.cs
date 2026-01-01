using System.Windows;

namespace PhoneRomFlashTool.Views
{
    public partial class HexEditorWindow : Window
    {
        public HexEditorWindow()
        {
            InitializeComponent();
        }

        public HexEditorWindow(string filePath) : this()
        {
            if (DataContext is ViewModels.HexEditorViewModel vm)
            {
                vm.OpenFileFromPath(filePath);
            }
        }
    }
}
