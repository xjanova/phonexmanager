using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PhoneRomFlashTool.Data;

namespace PhoneRomFlashTool.ViewModels
{
    public class GuidesViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<string>? LogMessage;

        public ObservableCollection<BootloaderGuide> BootloaderGuides { get; } = new();
        public ObservableCollection<FrpBypassGuide> FrpGuides { get; } = new();

        private BootloaderGuide? _selectedBootloaderGuide;
        public BootloaderGuide? SelectedBootloaderGuide
        {
            get => _selectedBootloaderGuide;
            set
            {
                _selectedBootloaderGuide = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedBootloaderGuide));
            }
        }

        private FrpBypassGuide? _selectedFrpGuide;
        public FrpBypassGuide? SelectedFrpGuide
        {
            get => _selectedFrpGuide;
            set
            {
                _selectedFrpGuide = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedFrpGuide));
            }
        }

        public bool HasSelectedBootloaderGuide => SelectedBootloaderGuide != null;
        public bool HasSelectedFrpGuide => SelectedFrpGuide != null;

        public ICommand OpenOfficialUrlCommand { get; }

        public GuidesViewModel()
        {
            OpenOfficialUrlCommand = new RelayCommandWithParam<string>(OpenUrl);
            LoadGuides();
        }

        private void LoadGuides()
        {
            BootloaderGuides.Clear();
            foreach (var guide in BootloaderGuideData.GetGuides())
            {
                BootloaderGuides.Add(guide);
            }

            FrpGuides.Clear();
            foreach (var guide in BootloaderGuideData.GetFrpGuides())
            {
                FrpGuides.Add(guide);
            }

            LogMessage?.Invoke(this, $"Loaded {BootloaderGuides.Count} bootloader guides and {FrpGuides.Count} FRP guides");
        }

        private void OpenUrl(string? url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                    LogMessage?.Invoke(this, $"Opening: {url}");
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke(this, $"Error opening URL: {ex.Message}");
                }
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
