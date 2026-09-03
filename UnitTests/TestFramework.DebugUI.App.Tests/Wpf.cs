using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Axiom.State;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Somewhere to build a control and look at what it drew.
/// </summary>
/// <remarks>
/// <para>
/// Everything else in this suite tests what the window decides; this is for what it <em>shows</em>,
/// which until now was checked by launching the tool and looking. That is a fine way to find a
/// problem and a useless way to keep it fixed: two rendering faults in this project — a marker
/// through the end of a duration, a picture that no surface could reach — survived every green suite
/// there was.
/// </para>
/// <para>
/// Two things make a WPF control constructible in a test, and both are the reason this class exists
/// rather than each test arranging its own. A control has to be built on a single-threaded-apartment
/// thread with a dispatcher, and xUnit gives every test a thread-pool thread instead; and a control
/// asking for a brush by name needs an <see cref="Application"/> whose resources hold the palette,
/// or every <c>FindResource</c> throws.
/// </para>
/// <para>
/// The palette is the tool's own dictionary, merged from the application assembly, so what the tests
/// see is the shipped one rather than a copy kept in step by hand. A plain
/// <see cref="Application"/> holds it rather than the tool's <c>App</c>: that class elects a single
/// instance and opens a window, and none of that belongs in a test run.
/// </para>
/// <para>
/// A default state store is made here too, because a control is bound to one from the moment it is
/// constructed — the panel that resolves a widget's file asks the store which run it is showing.
/// </para>
/// </remarks>
internal static class Wpf
{
    private static readonly object Gate = new();

    private static Dispatcher? dispatcher;

    /// <summary>Builds and inspects a control, on a thread where that is allowed.</summary>
    /// <param name="work">What to build and assert.</param>
    public static void Run(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        // Invoke rather than BeginInvoke: the exception an assertion throws has to come back out to
        // the test, and a fire-and-forget post would leave the test passing and the failure in a log.
        Ensure().Invoke(work);
    }

    /// <summary>Builds a control and brings something back from it.</summary>
    public static T Run<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return Ensure().Invoke(work);
    }

    /// <summary>
    /// Lays a control out as a window would, so what it drew has a size and a position.
    /// </summary>
    /// <remarks>
    /// Nothing in WPF has geometry until it has been measured and arranged. A control that was only
    /// constructed reports every bound as zero, which makes "these two do not overlap" true of
    /// everything and the assertion worthless.
    /// </remarks>
    /// <param name="element">What to lay out.</param>
    /// <param name="width">The width to lay it out at.</param>
    /// <param name="height">The height to lay it out at.</param>
    public static void Layout(FrameworkElement element, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    /// <summary>
    /// Where one element sits inside another, once both have been laid out.
    /// </summary>
    /// <remarks>
    /// In the ancestor's own coordinates, which is what makes two of these comparable — the question
    /// being asked of them is whether they occupy the same place.
    /// </remarks>
    public static Rect BoundsOf(FrameworkElement element, Visual within)
    {
        ArgumentNullException.ThrowIfNull(element);

        GeneralTransform transform = element.TransformToAncestor(within);

        return transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    }

    /// <summary>
    /// Every descendant of a kind, in the order the tree holds them.
    /// </summary>
    /// <remarks>
    /// The visual tree rather than the logical one: what a reader sees is what was realised, and a
    /// control that decided to show nothing has nothing here to find.
    /// </remarks>
    public static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        ArgumentNullException.ThrowIfNull(root);

        int children = VisualTreeHelper.GetChildrenCount(root);

        for (int index = 0; index < children; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);

            if (child is T found)
                yield return found;

            foreach (T deeper in Descendants<T>(child))
                yield return deeper;
        }
    }

    /// <summary>
    /// The one thread this suite builds controls on, started on first use.
    /// </summary>
    /// <remarks>
    /// One for the whole suite rather than one per test, because an <see cref="Application"/> can be
    /// created once per process and a second attempt throws. The thread is a background one so a
    /// finished test run is not held open by a message loop nobody is talking to.
    /// </remarks>
    private static Dispatcher Ensure()
    {
        lock (Gate)
        {
            if (dispatcher is not null)
                return dispatcher;

            using ManualResetEventSlim ready = new(false);
            Dispatcher? started = null;

            Thread thread = new(() =>
            {
                Application application = new();

                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/TestFramework.DebugUI;component/Theme/Theme.xaml")
                });

                // Bound to before it is read: a control subscribes to the store as it is built, so
                // there has to be one by the time any test constructs anything.
                MainStore.Create().BuildAndMakeDefault();

                started = Dispatcher.CurrentDispatcher;
                ready.Set();

                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "WPF test harness"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            ready.Wait(TimeSpan.FromSeconds(30));

            dispatcher = started ?? throw new InvalidOperationException("The WPF test harness did not start.");

            return dispatcher;
        }
    }
}
