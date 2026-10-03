using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;

// The few WPF geometry types plugins use for hit testing.
namespace System.Windows
{
	public struct Point
	{
		public Point (double x, double y) { X = x; Y = y; }
		public double X { get; set; }
		public double Y { get; set; }
	}

	public struct Vector
	{
		public Vector (double x, double y) { X = x; Y = y; }
		public double X { get; set; }
		public double Y { get; set; }
		public double Length => Math.Sqrt (X * X + Y * Y);
	}
}

namespace System.Windows.Media
{
	public abstract class Geometry
	{
		public abstract bool FillContains (Point hitPoint);
	}

	public sealed class EllipseGeometry : Geometry
	{
		public EllipseGeometry () { }
		public EllipseGeometry (Point center, double radiusX, double radiusY) { Center = center; RadiusX = radiusX; RadiusY = radiusY; }
		public Point Center { get; set; }
		public double RadiusX { get; set; }
		public double RadiusY { get; set; }

		public override bool FillContains (Point p)
		{
			if (RadiusX <= 0 || RadiusY <= 0) return false;
			double dx = (p.X - Center.X) / RadiusX, dy = (p.Y - Center.Y) / RadiusY;
			return dx * dx + dy * dy <= 1;
		}
	}

	public sealed class RectangleGeometry : Geometry
	{
		public RectangleGeometry () { }
		public double X { get; set; }
		public double Y { get; set; }
		public double Width { get; set; }
		public double Height { get; set; }
		public override bool FillContains (Point p) => p.X >= X && p.Y >= Y && p.X <= X + Width && p.Y <= Y + Height;
	}
}

// Inert stand-ins for the Windows Forms types that effect plugins touch on paths that do run:
// CodeLab's help window boilerplate, MessageBox calls for errors, and small progress forms.
// Nothing is shown; forms report Cancel and MessageBox writes to standard error.
namespace System.Windows.Forms
{
	public interface IWin32Window
	{
		IntPtr Handle { get; }
	}

	public enum DialogResult { None = 0, OK = 1, Cancel = 2, Abort = 3, Retry = 4, Ignore = 5, Yes = 6, No = 7, TryAgain = 10, Continue = 11 }
	public enum MessageBoxButtons { OK = 0, OKCancel = 1, AbortRetryIgnore = 2, YesNoCancel = 3, YesNo = 4, RetryCancel = 5, CancelTryContinue = 6 }
	public enum MessageBoxIcon { None = 0, Hand = 16, Stop = 16, Error = 16, Question = 32, Exclamation = 48, Warning = 48, Asterisk = 64, Information = 64 }
	public enum MessageBoxDefaultButton { Button1 = 0, Button2 = 256, Button3 = 512 }
	[Flags] public enum AnchorStyles { None = 0, Top = 1, Bottom = 2, Left = 4, Right = 8 }
	public enum AutoScaleMode { None = 0, Font = 1, Dpi = 2, Inherit = 3 }
	public enum DockStyle { None = 0, Top = 1, Bottom = 2, Left = 3, Right = 4, Fill = 5 }
	public enum FormBorderStyle { None = 0, FixedSingle = 1, Fixed3D = 2, FixedDialog = 3, Sizable = 4, FixedToolWindow = 5, SizableToolWindow = 6 }
	public enum FormStartPosition { Manual = 0, CenterScreen = 1, WindowsDefaultLocation = 2, WindowsDefaultBounds = 3, CenterParent = 4 }
	public enum RichTextBoxScrollBars { None = 0, Horizontal = 1, Vertical = 2, Both = 3, ForcedHorizontal = 17, ForcedVertical = 18, ForcedBoth = 19 }
	public enum ScrollBars { None = 0, Horizontal = 1, Vertical = 2, Both = 3 }
	public enum BorderStyle { None = 0, FixedSingle = 1, Fixed3D = 2 }
	public enum ToolStripGripStyle { Hidden = 0, Visible = 1 }
	public enum ComboBoxStyle { Simple = 0, DropDown = 1, DropDownList = 2 }
	public enum HorizontalAlignment { Left = 0, Right = 1, Center = 2 }
	[Flags] public enum RichTextBoxFinds { None = 0, WholeWord = 2, MatchCase = 4, NoHighlight = 8, Reverse = 16 }
	public enum CloseReason { None = 0, WindowsShutDown = 1, MdiFormClosing = 2, UserClosing = 3, TaskManagerClosing = 4, FormOwnerClosing = 5, ApplicationExitCall = 6 }

	public sealed class LinkClickedEventArgs : EventArgs
	{
		public LinkClickedEventArgs (string linkText) { LinkText = linkText; }
		public string LinkText { get; }
	}

	public delegate void LinkClickedEventHandler (object sender, LinkClickedEventArgs e);

	public class FormClosedEventArgs : EventArgs
	{
		public FormClosedEventArgs (CloseReason closeReason) { CloseReason = closeReason; }
		public CloseReason CloseReason { get; }
	}

	public delegate void FormClosedEventHandler (object sender, FormClosedEventArgs e);

	public class PaintEventArgs : EventArgs, IDisposable
	{
		public PaintEventArgs (Graphics graphics, Rectangle clipRect) { Graphics = graphics; ClipRectangle = clipRect; }
		public Graphics Graphics { get; }
		public Rectangle ClipRectangle { get; }
		public void Dispose () { }
	}

	public class Control : Component, IWin32Window
	{
		public Control () { Controls = new ControlCollection (this); }
		public IntPtr Handle => IntPtr.Zero;
		public ControlCollection Controls { get; }
		public string Text { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;
		public Font Font { get; set; }
		public Color ForeColor { get; set; }
		public Color BackColor { get; set; }
		public Point Location { get; set; }
		public Size Size { get; set; }
		public Size MinimumSize { get; set; }
		public Size MaximumSize { get; set; }
		public int Width { get => Size.Width; set => Size = new Size (value, Size.Height); }
		public int Height { get => Size.Height; set => Size = new Size (Size.Width, value); }
		public bool Enabled { get; set; } = true;
		public bool Visible { get; set; }
		public bool AutoSize { get; set; }
		public AnchorStyles Anchor { get; set; }
		public DockStyle Dock { get; set; }
		public int TabIndex { get; set; }
		public object Tag { get; set; }
		public event EventHandler Click;
		public event EventHandler TextChanged;
		public bool Focus () => false;
		public void Invalidate () { }
		public void Refresh () { }
		public void SuspendLayout () { }
		public void ResumeLayout () { }
		public void ResumeLayout (bool performLayout) { }
		public void PerformLayout () { }
		public void Show () { }
		public void Hide () { }
		public void Invoke (Action method) => method ();
		public object Invoke (Delegate method) => method.DynamicInvoke ();
		public object Invoke (Delegate method, params object[] args) => method.DynamicInvoke (args);
		public IAsyncResult BeginInvoke (Delegate method) { method.DynamicInvoke (); return null; }
		public bool InvokeRequired => false;
		protected virtual void OnClick (EventArgs e) => Click?.Invoke (this, e);
		protected virtual void OnTextChanged (EventArgs e) => TextChanged?.Invoke (this, e);
		protected virtual void OnMouseEnter (EventArgs e) { }
		protected virtual void OnMouseLeave (EventArgs e) { }
		protected virtual void OnMove (EventArgs e) { }
		protected virtual void OnResize (EventArgs e) { }
		protected virtual void OnPaint (PaintEventArgs e) { }

		public class ControlCollection : List<Control>
		{
			public ControlCollection (Control owner) { Owner = owner; }
			public Control Owner { get; }
			public void SetChildIndex (Control child, int newIndex) { }
		}
	}

	public class ScrollableControl : Control
	{
		public bool AutoScroll { get; set; }
	}

	public class ContainerControl : ScrollableControl
	{
		public SizeF AutoScaleDimensions { get; set; }
		public AutoScaleMode AutoScaleMode { get; set; }
	}

	public class UserControl : ContainerControl { }

	public class Panel : ScrollableControl { }

	public class FlowLayoutPanel : Panel { }

	public class Label : Control { }

	public class ProgressBar : Control
	{
		public int Minimum { get; set; }
		public int Maximum { get; set; } = 100;
		public int Value { get; set; }
	}

	public interface IButtonControl
	{
		DialogResult DialogResult { get; set; }
		void PerformClick ();
		void NotifyDefault (bool value);
	}

	public abstract class ButtonBase : Control
	{
		public bool UseCompatibleTextRendering { get; set; }
		public bool UseVisualStyleBackColor { get; set; }
	}

	public class Button : ButtonBase, IButtonControl
	{
		public DialogResult DialogResult { get; set; }
		public void PerformClick () => OnClick (EventArgs.Empty);
		public void NotifyDefault (bool value) { }
	}

	public abstract class TextBoxBase : Control
	{
		public bool ReadOnly { get; set; }
		public bool WordWrap { get; set; } = true;
		public bool Multiline { get; set; }
		public bool AcceptsTab { get; set; }
		public int MaxLength { get; set; } = 32767;
		public BorderStyle BorderStyle { get; set; }
		public string SelectedText { get; set; } = string.Empty;
		public void Select (int start, int length) { }
		public void SelectAll () { }
	}

	public class TextBox : TextBoxBase
	{
		public ScrollBars ScrollBars { get; set; }
	}

	public class RichTextBox : TextBoxBase
	{
		public string Rtf { get; set; } = string.Empty;
		public bool DetectUrls { get; set; }
		public RichTextBoxScrollBars ScrollBars { get; set; }
		public Font SelectionFont { get; set; }
		public Color SelectionColor { get; set; }
		public Color SelectionBackColor { get; set; }
		public int SelectionIndent { get; set; }
		public int SelectionRightIndent { get; set; }
		public int SelectionCharOffset { get; set; }
		public HorizontalAlignment SelectionAlignment { get; set; }
		public event LinkClickedEventHandler LinkClicked;
		public int Find (string str) => -1;
		public int Find (string str, int start, RichTextBoxFinds options) => -1;
		protected virtual void OnLinkClicked (LinkClickedEventArgs e) => LinkClicked?.Invoke (this, e);
	}

	public class Form : ContainerControl
	{
		public static Form ActiveForm { get; } = new ();
		public Size ClientSize { get; set; }
		public FormBorderStyle FormBorderStyle { get; set; }
		public FormStartPosition StartPosition { get; set; }
		public Icon Icon { get; set; }
		public bool MinimizeBox { get; set; }
		public bool MaximizeBox { get; set; }
		public bool ShowIcon { get; set; }
		public bool ShowInTaskbar { get; set; }
		public bool TopMost { get; set; }
		public double Opacity { get; set; } = 1;
		public DialogResult DialogResult { get; set; }
		public IButtonControl AcceptButton { get; set; }
		public IButtonControl CancelButton { get; set; }
		public event EventHandler Load;
		public event FormClosedEventHandler FormClosed;

		/// <summary>No window can be shown on this platform: the dialog is cancelled.</summary>
		public DialogResult ShowDialog () { Load?.Invoke (this, EventArgs.Empty); FormClosed?.Invoke (this, new FormClosedEventArgs (CloseReason.None)); return DialogResult.Cancel; }
		public DialogResult ShowDialog (IWin32Window owner) => ShowDialog ();
		public void Close () { }
		public void Activate () { }
	}

	public abstract class CommonDialog : Component
	{
		public DialogResult ShowDialog () => DialogResult.Cancel;
		public DialogResult ShowDialog (IWin32Window owner) => DialogResult.Cancel;
	}

	public sealed class ColorDialog : CommonDialog
	{
		public Color Color { get; set; }
		public bool FullOpen { get; set; }
		public bool AllowFullOpen { get; set; }
	}

	public abstract class FileDialog : CommonDialog
	{
		public string FileName { get; set; } = string.Empty;
		public string Filter { get; set; } = string.Empty;
		public string Title { get; set; } = string.Empty;
		public string DefaultExt { get; set; } = string.Empty;
		public string InitialDirectory { get; set; } = string.Empty;
	}

	public sealed class OpenFileDialog : FileDialog { public bool Multiselect { get; set; } }

	public sealed class SaveFileDialog : FileDialog { public bool OverwritePrompt { get; set; } }

	public static class MessageBox
	{
		private static DialogResult Log (string text, string caption)
		{
			Console.Error.WriteLine ($"Paint.NET plugin message: {caption}: {text}");
			return DialogResult.OK;
		}

		public static DialogResult Show (string text) => Log (text, string.Empty);
		public static DialogResult Show (string text, string caption) => Log (text, caption);
		public static DialogResult Show (string text, string caption, MessageBoxButtons buttons) => Log (text, caption);
		public static DialogResult Show (string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Log (text, caption);
		public static DialogResult Show (string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton) => Log (text, caption);
		public static DialogResult Show (IWin32Window owner, string text) => Log (text, string.Empty);
		public static DialogResult Show (IWin32Window owner, string text, string caption) => Log (text, caption);
		public static DialogResult Show (IWin32Window owner, string text, string caption, MessageBoxButtons buttons) => Log (text, caption);
		public static DialogResult Show (IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Log (text, caption);
	}

	public static class Application
	{
		public static void DoEvents () { }
		public static string StartupPath => AppContext.BaseDirectory;
		public static string ProductVersion => "5.2";
		public static List<Form> OpenForms { get; } = [];
		public static event EventHandler ApplicationExit { add { } remove { } }
		public static event EventHandler ThreadExit { add { } remove { } }
		public static event EventHandler Idle { add { } remove { } }
	}

	public static class TextRenderer
	{
		public static Size MeasureText (string text, Font font)
		{
			using Bitmap b = new (1, 1);
			using Graphics g = Graphics.FromImage (b);
			return Size.Ceiling (g.MeasureString (text, font));
		}
	}

	public sealed class ToolTip : Component
	{
		private readonly Dictionary<Control, string> tips = [];
		public void SetToolTip (Control control, string caption) => tips[control] = caption;
		public string GetToolTip (Control control) => tips.TryGetValue (control, out string s) ? s : string.Empty;
	}
}
