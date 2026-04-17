using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WPFCommon.SlidingSelectPanel
{
    /// <summary>
    /// Interaction logic for UC_SLP_Base.xaml
    /// </summary>
    public partial class UC_SLP_Base : UserControl
    {
        public delegate void OnIndexChangeHandler(object sender, int index);

        public Brush CursorColor
        {
            get { return (Brush)GetValue(CursorColorProperty); }
            set { SetValue(CursorColorProperty, value); }
        }

        public static readonly DependencyProperty CursorColorProperty =
            DependencyProperty.Register("CursorColor", typeof(Brush), typeof(UC_SLP_Base), new PropertyMetadata(new SolidColorBrush(Colors.White)));

        public string Titles
        {
            get { return (string)GetValue(TitlesProperty); }
            set { SetValue(TitlesProperty, value); }
        }

        public static readonly DependencyProperty TitlesProperty =
            DependencyProperty.Register("Titles", typeof(string), typeof(UC_SLP_Base), new PropertyMetadata("", (d,e) => ((UC_SLP_Base)d).OnTitles()));

        public OnIndexChangeHandler OnIndexChange
        {
            get { return (OnIndexChangeHandler)GetValue(OnIndexChangeProperty); }
            set { SetValue(OnIndexChangeProperty, value); }
        }

        public static readonly DependencyProperty OnIndexChangeProperty =
            DependencyProperty.Register("OnIndexChange", typeof(OnIndexChangeHandler), typeof(UC_SLP_Base), new PropertyMetadata(null));

        public int Index
        {
            get { return (int)GetValue(IndexProperty); }
            set { SetValue(IndexProperty, value); }
        }

        public static readonly DependencyProperty IndexProperty =
            DependencyProperty.Register("Index", typeof(int), typeof(UC_SLP_Base), new PropertyMetadata(0, (d, e) => ((UC_SLP_Base)d).SetIndex(((UC_SLP_Base)d).Index)));

        public UC_SLP_Base()
        {
            InitializeComponent();
        }

        public void SetIndex(int i)
        {
            double lOffset = spHead.Children[i].TranslatePoint(new Point(0, 0), spHead).X;
            DoubleAnimation lAnim = new DoubleAnimation
            {
                From = Canvas.GetLeft(bHeadCursor),
                To = lOffset,
                Duration = new Duration(TimeSpan.FromSeconds(0.2))
            };
            Storyboard.SetTarget(lAnim, bHeadCursor);
            Storyboard.SetTargetProperty(lAnim, new PropertyPath("(Canvas.Left)"));
            Storyboard stLo = new Storyboard();
            stLo.Children.Add(lAnim);

            DoubleAnimation wAnim = new DoubleAnimation
            {
                From = bHeadCursor.ActualWidth,
                To = ((FrameworkElement)spHead.Children[i]).ActualWidth - 10,
                Duration = new Duration(TimeSpan.FromSeconds(0.2))
            };
            Storyboard.SetTarget(wAnim, bHeadCursor);
            Storyboard.SetTargetProperty(wAnim, new PropertyPath(Border.WidthProperty));
            Storyboard stWi = new Storyboard();
            stWi.Children.Add(wAnim);

            stWi.Begin();
            stLo.Begin();

            OnIndexChange?.Invoke(this, i);
        }

        private void OnTitles()
        {
            string[] ts = Titles.Split(';');
            spHead.Children.Clear();
            for (int i = 0; i < ts.Length; i++)
            {
                int ind = i;
                spHead.Children.Add(new UC_SLP_Title
                {
                    Title = ts[i],
                    Click = (_, _) => { SetIndex(ind); },
                });
            }
        }

        private void usercontrol_Loaded(object sender, RoutedEventArgs e)
        {
            SetIndex(Index);
        }
    }
}