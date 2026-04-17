using System;
using System.Collections.Generic;
using System.Drawing;
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

namespace WPFCommon.DropDown
{
    /// <summary>
    /// Interaction logic for UC_DD_Header.xaml
    /// </summary>
    public partial class UC_DD_Header : UserControl
    {
        public StackPanel Content
        {
            get { return (StackPanel)GetValue(ContentProperty); }
            set { SetValue(ContentProperty, value); }
        }

        public static readonly DependencyProperty ContentProperty =
            DependencyProperty.Register("Content", typeof(StackPanel), typeof(UC_DD_Header), new PropertyMetadata(null, UpdateOwnContent));

        public bool IsOpen
        {
            get { return (bool)GetValue(IsOpenProperty); }
            set { SetValue(IsOpenProperty, value); }
        }

        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register("IsOpen", typeof(bool), typeof(UC_DD_Header), new PropertyMetadata(false, UpdateOwnIsOpen));

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(UC_DD_Header), new PropertyMetadata(""));

        public bool UseIcon
        {
            get { return (bool)GetValue(UseIconProperty); }
            set { SetValue(UseIconProperty, value); }
        }

        public static readonly DependencyProperty UseIconProperty =
            DependencyProperty.Register("UseIcon", typeof(bool), typeof(UC_DD_Header), new PropertyMetadata(false, UpdateOwnUseIcon));

        public ImageSource Icon
        {
            get { return (ImageSource)GetValue(IconProperty); }
            set { SetValue(IconProperty, value); }
        }

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(ImageSource), typeof(UC_DD_Header), new PropertyMetadata(null));

        public Canvas IconCanvas
        {
            get { return (Canvas)GetValue(IconCanvasProperty); }
            set { SetValue(IconCanvasProperty, value); }
        }

        public static readonly DependencyProperty IconCanvasProperty =
            DependencyProperty.Register("IconCanvas", typeof(Canvas), typeof(UC_DD_Header), new PropertyMetadata(null, UpdateOwnIconCanvas));

		public RoutedEventHandler Get
		{
			get { return (RoutedEventHandler)GetValue(GetProperty); }
			set { SetValue(GetProperty, value); }
		}

		public static readonly DependencyProperty GetProperty =
			DependencyProperty.Register("Get", typeof(RoutedEventHandler), typeof(UC_DD_Header), new PropertyMetadata(null, (d, e) => ((UC_DD_Header)d).Get.Invoke((UC_DD_Header)d, null)));

		public static void UpdateOwnIsOpen(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as UC_DD_Header).UpdateIsOpen();
        }

        public static void UpdateOwnContent(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as UC_DD_Header).UpdateContent();
        }

        public static void UpdateOwnUseIcon(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as UC_DD_Header).UpdateUseIcon();
        }

        public static void UpdateOwnIconCanvas(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as UC_DD_Header).UpdateIconCanvas();
        }

        public UC_DD_Header()
        {
            InitializeComponent();
        }

        public void UpdateIsOpen()
        {
            if (IsOpen)
            {
                rdContent.Height = new GridLength(1, GridUnitType.Auto);
                border.Style = null;
                border.BorderThickness = new Thickness(2);
            }
            else
            {
                rdContent.Height = new GridLength(0);
                border.Style = (Style)FindResource("border");
                border.BorderThickness = new Thickness(0);
            }
            
        }

        public void UpdateUseIcon()
        {
            if (UseIcon)
            {
                cdIcon.Width = new GridLength(border.ActualHeight);
                //Binding b = new Binding();
                //b.Source = border;
                //b.Path = new PropertyPath(Border.ActualHeightProperty);
                //b.Mode = BindingMode.OneWay;
                //cdIcon.SetBinding(ColumnDefinition.WidthProperty, b);
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

        public void UpdateContent()
        {
            bContent.Child = Content;
        }

        private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            IsOpen = !IsOpen;
        }

        double _borderActualHeight = -1;
        private void userControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (UseIcon)
            {
                if (_borderActualHeight == -1) _borderActualHeight = border.ActualHeight;
				cdIcon.Width = new GridLength(_borderActualHeight);
            }
        }
    }
}