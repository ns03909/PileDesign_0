using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PileDesignCore
{
    public class AxialForceViewModel : INotifyPropertyChanged
    {
        private bool isReplaceValueMode;
        private bool isAddValueMode;
        private double axialForce;
        private ObservableCollection<string> loadCases;
        private string selectedLoadCase;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsReplaceValueMode
        {
            get { return isReplaceValueMode; }
            set
            {
                if (isReplaceValueMode != value)
                {
                    isReplaceValueMode = value;
                    OnPropertyChanged(nameof(IsReplaceValueMode));
                }
            }
        }

        public bool IsAddValueMode
        {
            get { return isAddValueMode; }
            set
            {
                if (isAddValueMode != value)
                {
                    isAddValueMode = value;
                    OnPropertyChanged(nameof(IsAddValueMode));
                }
            }
        }

        public double AxialForce
        {
            get { return axialForce; }
            set
            {
                if (axialForce != value)
                {
                    axialForce = value;
                    OnPropertyChanged(nameof(AxialForce));
                }
            }
        }

        public ObservableCollection<string> LoadCases
        {
            get { return loadCases; }
            set
            {
                if (loadCases != value)
                {
                    loadCases = value;
                    OnPropertyChanged(nameof(LoadCases));
                }
            }
        }

        public string SelectedLoadCase
        {
            get { return selectedLoadCase; }
            set
            {
                if (selectedLoadCase != value)
                {
                    selectedLoadCase = value;
                    OnPropertyChanged(nameof(SelectedLoadCase));
                }
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void ResetStatus()
        {
            IsReplaceValueMode = false;
            IsAddValueMode = false;
            AxialForce = 0.0;
            SelectedLoadCase = null;

        }
    }
}
