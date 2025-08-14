using System.Windows;
using System.Windows.Controls;
using System.Windows.Media; // Required for ImageSource if using icons

namespace EdgeAlertSignalClient.Views
{
    /// <summary>
    /// Interaction logic for DataCardView.xaml
    /// A simple UserControl to display a title and a value in a card format.
    /// </summary>
    public partial class DataCardView : UserControl
    {
        public DataCardView()
        {
            InitializeComponent();
        }

        // Dependency Property for Card Title
        public static readonly DependencyProperty CardTitleProperty =
            DependencyProperty.Register("CardTitle", typeof(string), typeof(DataCardView), new PropertyMetadata("Card Title"));

        public string CardTitle
        {
            get { return (string)GetValue(CardTitleProperty); }
            set { SetValue(CardTitleProperty, value); }
        }

        // Dependency Property for Card Value (Using string to accommodate formatted time spans or counts)
        public static readonly DependencyProperty CardValueProperty =
            DependencyProperty.Register("CardValue", typeof(string), typeof(DataCardView), new PropertyMetadata("0"));

        public string CardValue
        {
            get { return (string)GetValue(CardValueProperty); }
            set { SetValue(CardValueProperty, value); }
        }

        // Optional: Dependency Property for an Icon (using string for path, could use ImageSource too)
        // public static readonly DependencyProperty CardIconProperty =
        //     DependencyProperty.Register("CardIcon", typeof(string), typeof(DataCardView), new PropertyMetadata(null));
        //
        // public string CardIcon
        // {
        //     get { return (string)GetValue(CardIconProperty); }
        //     set { SetValue(CardIconProperty, value); }
        // }
    }
}