using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WpfCommon;

namespace WPFCommon.DropDown
{
    /// <summary>
    /// Interaction logic for UC_DD_Button.xaml
    /// </summary>
    public partial class UC_DD_Button : UserControl
    {
        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(UC_DD_Button), new PropertyMetadata(""));

        public bool UseIcon
        {
            get { return (bool)GetValue(UseIconProperty); }
            set { SetValue(UseIconProperty, value); }
        }

        public static readonly DependencyProperty UseIconProperty =
            DependencyProperty.Register("UseIcon", typeof(bool), typeof(UC_DD_Button), new PropertyMetadata(false, UpdateOwnUseIcon));

        public ImageSource Icon
        {
            get { return (ImageSource)GetValue(IconProperty); }
            set { SetValue(IconProperty, value); }
        }

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(ImageSource), typeof(UC_DD_Button), new PropertyMetadata(null));

        public Canvas IconCanvas
        {
            get { return (Canvas)GetValue(IconCanvasProperty); }
            set { SetValue(IconCanvasProperty, value); }
        }

        public static readonly DependencyProperty IconCanvasProperty =
            DependencyProperty.Register("IconCanvas", typeof(Canvas), typeof(UC_DD_Button), new PropertyMetadata(null, UpdateOwnIconCanvas));

        public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent("Click", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UC_DD_Button));

        public event RoutedEventHandler Click
        {
            add { AddHandler(ClickEvent, value); }
            remove { RemoveHandler(ClickEvent, value); }
        }

        public RoutedEventHandler Get
        {
            get { return (RoutedEventHandler)GetValue(GetProperty); }
            set { SetValue(GetProperty, value); }
        }

        public static readonly DependencyProperty GetProperty =
            DependencyProperty.Register("Get", typeof(RoutedEventHandler), typeof(UC_DD_Button), new PropertyMetadata(null, (d, e) => ((UC_DD_Button)d).Get.Invoke((UC_DD_Button)d, null)));

        public bool CloseOnClick
        {
            get { return (bool)GetValue(CloseOnClickProperty); }
            set { SetValue(CloseOnClickProperty, value); }
        }

        public static readonly DependencyProperty CloseOnClickProperty =
            DependencyProperty.Register("CloseOnClick", typeof(bool), typeof(UC_DD_Button), new PropertyMetadata(true));

        public static void UpdateOwnUseIcon(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as UC_DD_Button).UpdateUseIcon();
        }

        public static void UpdateOwnIconCanvas(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as UC_DD_Button).UpdateIconCanvas();
        }

        public UC_DD_Button()
        {
            InitializeComponent();
            Get?.Invoke(this, null);
        }

        public void UpdateUseIcon()
        {
            if (UseIcon)
            {
                //cdIcon.Width = new GridLength(border.ActualHeight);
                Binding b = new Binding();
                b.Source = border;
                b.Path = new PropertyPath(Border.ActualHeightProperty);
                b.Mode = BindingMode.OneWay;
                cdIcon.SetBinding(ColumnDefinition.WidthProperty, b);
            }
            else
            {
                cdIcon.Width = new GridLength(0);
            }
        }

        public void UpdateIconCanvas()
        {
            vbImage.Visibility = Visibility.Collapsed;
            vbIcon.Child = IconCanvas;
            vbIcon.Visibility = Visibility.Visible;
        }

        private void border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            RoutedEventArgs routedEventArgs = new(ClickEvent, this);
            RaiseEvent(routedEventArgs);

            Helper.FindParent<ContextMenu>(this).IsOpen = false;
        }
    }
}