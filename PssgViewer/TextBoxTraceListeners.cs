using System;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PssgViewer
{
    /// <summary>
    /// A trace listener that redirects Debug.WriteLine output to a TextBox control
    /// </summary>
    public class TextBoxTraceListener : TraceListener
    {
        private readonly TextBox _textBox;

        public TextBoxTraceListener(TextBox textBox)
        {
            _textBox = textBox ?? throw new ArgumentNullException(nameof(textBox));
        }

        public override void Write(string message)
        {
            if (_textBox != null && _textBox.Dispatcher != null)
            {
                _textBox.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    _textBox.AppendText(message);
                }));
            }
        }

        public override void WriteLine(string message)
        {
            if (_textBox != null && _textBox.Dispatcher != null)
            {
                _textBox.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    _textBox.AppendText(message + Environment.NewLine);
                    _textBox.ScrollToEnd();
                }));
            }
        }
    }
}