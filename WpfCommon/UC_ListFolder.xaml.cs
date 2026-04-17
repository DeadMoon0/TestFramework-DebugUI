using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WPFCommon
{
    /// <summary>
    /// Interaction logic for UC_ListFolder.xaml
    /// </summary>
    [ContentProperty(nameof(Children))]
    public partial class UC_ListFolder : UserControl
    {
        public bool IsOpen
        {
            get { return (bool)GetValue(IsOpenProperty); }
            set { SetValue(IsOpenProperty, value); }
        }

        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register("IsOpen", typeof(bool), typeof(UC_ListFolder), new PropertyMetadata(true, (d, e) => ((UC_ListFolder)d).OnIsOpen()));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(UC_ListFolder), new PropertyMetadata(""));

        public bool ShowCount
        {
            get { return (bool)GetValue(ShowCountProperty); }
            set { SetValue(ShowCountProperty, value); }
        }

        public static readonly DependencyProperty ShowCountProperty =
            DependencyProperty.Register("ShowCount", typeof(bool), typeof(UC_ListFolder), new PropertyMetadata(true, (d, e) => ((UC_ListFolder)d).OnShowCount()));

        public string ZeroCountText
        {
            get { return (string)GetValue(ZeroCountTextProperty); }
            set { SetValue(ZeroCountTextProperty, value); }
        }

        public static readonly DependencyProperty ZeroCountTextProperty =
            DependencyProperty.Register("ZeroCountText", typeof(string), typeof(UC_ListFolder), new PropertyMetadata("", (d,e) => ((UC_ListFolder)d).OnZeroCountText()));

        public ObservableUIElementCollection Children
        {
            get { return (ObservableUIElementCollection)GetValue(ChildrenProperty.DependencyProperty); }
            private set { SetValue(ChildrenProperty, value); }
        }

        public static readonly DependencyPropertyKey ChildrenProperty =
            DependencyProperty.RegisterReadOnly(nameof(Children), typeof(ObservableUIElementCollection), typeof(UC_ListFolder), new PropertyMetadata());

        public UC_ListFolder()
        {
            InitializeComponent();
            Children = (ObservableUIElementCollection)spContent.Children;
            Children.CollectionChanged += (_, _) =>
            {
                if(ZeroCountText != "" && Children.Count == 0)
                {
                    lZCount.Visibility = Visibility.Visible;
                    lZCount.Content = ZeroCountText;
                }
                else
                {
                    lZCount.Visibility = Visibility.Collapsed;
                }
                lCount.Content = Children.Count;
            };
        }

        private void bt_Click(object sender, RoutedEventArgs e)
        {
            IsOpen = !IsOpen;
        }

        private void OnIsOpen()
        {
            if (IsOpen)
            {
                gContent.Visibility = Visibility.Visible;
            }
            else
            {
                gContent.Visibility = Visibility.Collapsed;
            }
        }

        private void OnShowCount()
        {
            lCount.Visibility = ShowCount ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnZeroCountText()
        {
            if(ZeroCountText == "")
            {
                lZCount.Visibility = Visibility.Collapsed;
            }
            else
            {
                lZCount.Visibility = Visibility.Visible;
            }
        }
    }

    public class ObservableUIElementCollection : UIElementCollection, INotifyCollectionChanged, INotifyPropertyChanged
    {
        public ObservableUIElementCollection(UIElement visualParent, FrameworkElement logicalParent)
            : base(visualParent, logicalParent)
        {
        }

        public override int Add(UIElement element)
        {
            int index = base.Add(element);
            var args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, element, index);
            OnCollectionChanged(args);
            OnPropertyChanged("Count");
            OnPropertyChanged("Item[]");
            return index;
        }

        public override void Clear()
        {
            base.Clear();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            OnPropertyChanged("Count");
            OnPropertyChanged("Item[]");
        }

        public override void Insert(int index, UIElement element)
        {
            base.Insert(index, element);
            var args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, element, index);
            OnCollectionChanged(args);
            OnPropertyChanged("Count");
            OnPropertyChanged("Item[]");
        }

        public override void Remove(UIElement element)
        {
            int index = IndexOf(element);
            if (index >= 0)
            {
                RemoveAt(index);
                var args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, element, index);
                OnCollectionChanged(args);
                OnPropertyChanged("Count");
                OnPropertyChanged("Item[]");
            }
        }

        public override UIElement this[int index]
        {
            get
            {
                return base[index];
            }
            set
            {
                base[index] = value;
                var args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, index);
                OnCollectionChanged(args);
                OnPropertyChanged("Item[]");
            }
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            var handler = CollectionChanged;
            if (handler != null)
                handler(this, e);
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ObservableStackPanel : StackPanel
    {
        protected override UIElementCollection CreateUIElementCollection(FrameworkElement logicalParent)
        {
            return new ObservableUIElementCollection(this, logicalParent);
        }
    }
}