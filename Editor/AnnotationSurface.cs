using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kadr.Core;

namespace Kadr.Editor
{
    /// <summary>Visual host for annotation DrawingVisuals.</summary>
    sealed class AnnotationLayer : FrameworkElement
    {
        public readonly VisualCollection Visuals;
        public AnnotationLayer() { Visuals = new VisualCollection(this); IsHitTestVisible = false; }
        protected override int VisualChildrenCount => Visuals.Count;
        protected override Visual GetVisualChild(int index) => Visuals[index];
    }

    /// <summary>
    /// The drawing surface shared by the in-place overlay editor and the standalone editor window.
    /// The host forwards pointer input (in surface coordinates) and keyboard shortcuts.
    /// </summary>
    public sealed class AnnotationSurface : Grid
    {
        public static readonly Color Accent = Color.FromRgb(10, 132, 255);

        readonly AnnotationLayer _layer = new();
        readonly Canvas _textCanvas = new();
        readonly DrawingVisual _handles = new();
        readonly Dictionary<Annotation, DrawingVisual> _map = new();

        public readonly List<Annotation> Items = new();
        public RenderEnv Env = new();

        readonly Stack<List<Annotation>> _undo = new();
        readonly Stack<List<Annotation>> _redo = new();
        List<Annotation> _pending;

        Tool _tool = Tool.None;
        Color _color;
        int _size;
        TextStyleKind _textStyle;
        BlurKind _blurKind;

        Annotation _selected;
        enum Drag { None, Create, Move, Handle }
        Drag _drag;
        int _handleIndex;
        Point _last;
        Annotation _creating;

        TextBox _editor;
        TextAnnotation _editing;
        string _textBefore;
        bool _editingIsNew;

        public event Action StateChanged;      // tool/style/undo availability
        public event Action ContentChanged;

        public AnnotationSurface()
        {
            Children.Add(_layer);
            Children.Add(_textCanvas);
            _layer.Visuals.Add(_handles);
            var s = Settings.Current;
            try { _color = (Color)ColorConverter.ConvertFromString(s.LastColor); } catch { _color = Palette[0]; }
            _size = Math.Clamp(s.LastSize, 0, 2);
            _textStyle = (TextStyleKind)Math.Clamp(s.TextStyle, 0, 2);
            _blurKind = (BlurKind)Math.Clamp(s.BlurKind, 0, 3);
            ClipToBounds = false;
        }

        public static readonly Color[] Palette =
        {
            Color.FromRgb(255, 59, 48), Color.FromRgb(255, 149, 0), Color.FromRgb(255, 204, 0), Color.FromRgb(52, 199, 89),
            Color.FromRgb(10, 132, 255), Color.FromRgb(175, 82, 222), Color.FromRgb(255, 45, 85), Colors.White, Color.FromRgb(28, 28, 30),
        };

        public Tool Tool
        {
            get => _tool;
            set
            {
                if (_tool == value) return;
                EndTextEdit();
                _tool = value;
                StateChanged?.Invoke();
            }
        }

        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                Settings.Current.LastColor = value.ToString();
                ApplyToSelection(a => a.Color = value);
                StateChanged?.Invoke();
            }
        }

        public int SizeLevel
        {
            get => _size;
            set
            {
                _size = Math.Clamp(value, 0, 2);
                Settings.Current.LastSize = _size;
                ApplyToSelection(a => a.SizeLevel = _size);
                StateChanged?.Invoke();
            }
        }

        public TextStyleKind TextStyle
        {
            get => _textStyle;
            set
            {
                _textStyle = value;
                Settings.Current.TextStyle = (int)value;
                ApplyToSelection(a => { if (a is TextAnnotation t) t.Style = value; });
                StateChanged?.Invoke();
            }
        }

        public BlurKind BlurKind
        {
            get => _blurKind;
            set
            {
                _blurKind = value;
                Settings.Current.BlurKind = (int)value;
                ApplyToSelection(a => { if (a is BlurAnnotation b) b.Kind = value; });
                RebuildAll();
                StateChanged?.Invoke();
            }
        }

        public Annotation Selected => _selected;
        public bool IsEditingText => _editor != null;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public bool HasContent => Items.Count > 0;
        public bool IsDragging => _drag != Drag.None;

        // ------------------------------------------------------------------ rendering

        void Refresh(Annotation a)
        {
            if (!_map.TryGetValue(a, out var v))
            {
                v = new DrawingVisual();
                if (a.HasShadow) v.Effect = Annotation.Shadow;
                _map[a] = v;
                // backdrop effects (focus) sit under every other mark
                _layer.Visuals.Insert(IsBackdrop(a) ? 0 : _layer.Visuals.IndexOf(_handles), v);
            }
            v.Effect = a.HasShadow ? Annotation.Shadow : null;
            using (var dc = v.RenderOpen()) a.Render(dc, Env);
        }

        static bool IsBackdrop(Annotation a) => a is BlurAnnotation { Kind: BlurKind.Focus };
        static IEnumerable<Annotation> Ordered(IEnumerable<Annotation> items) => items.OrderBy(a => IsBackdrop(a) ? 0 : 1);

        void RebuildAll()
        {
            foreach (var v in _map.Values) _layer.Visuals.Remove(v);
            _map.Clear();
            foreach (var a in Ordered(Items)) Refresh(a);
            RefreshHandles();
        }

        void RefreshHandles()
        {
            using var dc = _handles.RenderOpen();
            var a = _selected;
            if (a == null || !Items.Contains(a) || _drag == Drag.Create) return;
            var hs = a.Handles;
            if (hs.Length == 0 || a is TextAnnotation)
            {
                if (a == _editing) return;
                var r = a.Bounds; r.Inflate(4, 4);
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
                var pen2 = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 1);
                dc.DrawRoundedRectangle(null, pen2, r, 4, 4);
                dc.DrawRoundedRectangle(null, pen, r, 4, 4);
                return;
            }
            var fill = new SolidColorBrush(Colors.White);
            var border = new Pen(new SolidColorBrush(Accent), 1.5);
            var shadow = new Pen(new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)), 3);
            for (int i = 0; i < hs.Length; i++)
            {
                bool mid = a is ArrowAnnotation && i == 2;
                double r = mid ? 4 : 4.5;
                dc.DrawEllipse(null, shadow, hs[i], r + 0.5, r + 0.5);
                dc.DrawEllipse(mid ? new SolidColorBrush(Accent) : fill, mid ? new Pen(fill, 1.5) : border, hs[i], r, r);
            }
        }

        // ------------------------------------------------------------------ undo

        List<Annotation> Snapshot() => Items.Select(a => a.Clone()).ToList();

        void BeginChange() => _pending = Snapshot();

        void CommitChange()
        {
            if (_pending == null) return;
            _undo.Push(_pending);
            _redo.Clear();
            _pending = null;
            StateChanged?.Invoke();
            ContentChanged?.Invoke();
        }

        void Restore(List<Annotation> state)
        {
            Items.Clear();
            Items.AddRange(state);
            _selected = null;
            RebuildAll();
            StateChanged?.Invoke();
            ContentChanged?.Invoke();
        }

        public void Undo()
        {
            EndTextEdit();
            if (_undo.Count == 0) return;
            _redo.Push(Snapshot());
            Restore(_undo.Pop());
        }

        public void Redo()
        {
            EndTextEdit();
            if (_redo.Count == 0) return;
            _undo.Push(Snapshot());
            Restore(_redo.Pop());
        }

        public void DeleteSelected()
        {
            if (_selected == null || IsEditingText) return;
            BeginChange();
            Remove(_selected);
            _selected = null;
            RefreshHandles();
            CommitChange();
        }

        public void Select(Annotation a)
        {
            _selected = a;
            RefreshHandles();
            StateChanged?.Invoke();
        }

        public void Deselect()
        {
            EndTextEdit();
            _selected = null;
            RefreshHandles();
        }

        void Remove(Annotation a)
        {
            Items.Remove(a);
            if (_map.TryGetValue(a, out var v)) { _layer.Visuals.Remove(v); _map.Remove(a); }
        }

        void ApplyToSelection(Action<Annotation> change)
        {
            var a = _selected;
            if (a == null || !Items.Contains(a)) return;
            if (a == _editing) { change(a); Refresh(a); ApplyEditorStyle(); return; }
            BeginChange();
            change(a);
            Refresh(a);
            RefreshHandles();
            CommitChange();
        }

        /// <summary>Load pre-existing annotations (e.g. when re-opening a capture in the editor).</summary>
        public void Load(IEnumerable<Annotation> items)
        {
            Items.Clear();
            Items.AddRange(items);
            RebuildAll();
        }

        // ------------------------------------------------------------------ pointer

        Annotation HitItem(Point p)
        {
            for (int i = Items.Count - 1; i >= 0; i--)
                if (Items[i].HitTest(p, 3)) return Items[i];
            return null;
        }

        int HitHandle(Point p)
        {
            if (_selected == null) return -1;
            var hs = _selected.Handles;
            for (int i = 0; i < hs.Length; i++)
                if ((hs[i] - p).Length <= 9) return i;
            return -1;
        }

        /// <summary>True if a press at p would be handled by the surface.</summary>
        public bool WantsPointer(Point p) => Tool != Tool.None || HitHandle(p) >= 0 || HitItem(p) != null || IsEditingText;

        public Cursor CursorAt(Point p)
        {
            if (HitHandle(p) >= 0) return Cursors.SizeAll;
            if (HitItem(p) != null) return Cursors.SizeAll;
            if (Tool == Tool.Text) return Cursors.IBeam;
            return null;
        }

        public bool OnDown(Point p, ModifierKeys mods, int clicks)
        {
            if (IsEditingText)
            {
                if (_editing != null && _editing.HitTest(p, 4)) return true;
                EndTextEdit();
            }

            int h = HitHandle(p);
            if (h >= 0)
            {
                BeginChange();
                _drag = Drag.Handle; _handleIndex = h; _last = p;
                return true;
            }

            var hit = HitItem(p);
            if (hit != null)
            {
                _selected = hit;
                if (hit is TextAnnotation ta && (clicks >= 2 || Tool == Tool.Text))
                {
                    BeginTextEdit(ta, false);
                    RefreshHandles();
                    return true;
                }
                BeginChange();
                _drag = Drag.Move; _last = p;
                RefreshHandles();
                StateChanged?.Invoke();
                return true;
            }

            if (Tool == Tool.None)
            {
                if (_selected != null) { _selected = null; RefreshHandles(); }
                return false;
            }

            _selected = null;
            BeginChange();
            Annotation a = Tool switch
            {
                Tool.Arrow => new ArrowAnnotation { Start = p, End = p },
                Tool.Rect => new ShapeAnnotation { A = p, B2 = p },
                Tool.Ellipse => new ShapeAnnotation { A = p, B2 = p, IsEllipse = true },
                Tool.Pen => new PenAnnotation(),
                Tool.Marker => new PenAnnotation { Marker = true },
                Tool.Blur => new BlurAnnotation { A = p, B2 = p, Kind = _blurKind },
                Tool.Counter => new CounterAnnotation { Center = p, Number = NextCounter() },
                Tool.Text => new TextAnnotation { Style = _textStyle },
                _ => null,
            };
            if (a == null) return false;
            a.Color = _color;
            a.SizeLevel = _size;
            if (a is PenAnnotation pen) pen.Points.Add(p);
            Items.Add(a);

            if (a is TextAnnotation t)
            {
                t.Position = new Point(p.X - 2, p.Y - t.FontSize * 0.68);
                _pending = null;
                _selected = t;
                BeginTextEdit(t, true);
                return true;
            }

            _creating = a;
            _drag = a is CounterAnnotation ? Drag.Move : Drag.Create;
            _selected = a;
            _last = p;
            Refresh(a);
            RefreshHandles();
            return true;
        }

        int NextCounter() => Items.OfType<CounterAnnotation>().Select(c => c.Number).DefaultIfEmpty(0).Max() + 1;

        public void OnMove(Point p, ModifierKeys mods)
        {
            bool shift = (mods & ModifierKeys.Shift) != 0;
            switch (_drag)
            {
                case Drag.Handle:
                    _selected.SetHandle(_handleIndex, p, shift);
                    Refresh(_selected);
                    RefreshHandles();
                    break;
                case Drag.Move:
                    _selected.Move(p - _last);
                    _last = p;
                    Refresh(_selected);
                    RefreshHandles();
                    break;
                case Drag.Create:
                    switch (_creating)
                    {
                        case ArrowAnnotation ar: ar.End = shift ? Annotation.SnapAngle(ar.Start, p) : p; break;
                        case ShapeAnnotation sh: sh.B2 = shift ? ShapeAnnotation.Square(sh.A, p) : p; break;
                        case BlurAnnotation bl: bl.B2 = shift ? ShapeAnnotation.Square(bl.A, p) : p; break;
                        case PenAnnotation pn:
                            if (shift && pn.Points.Count > 0)
                            {
                                var s = pn.Points[0];
                                pn.Points.RemoveRange(1, pn.Points.Count - 1);
                                pn.Points.Add(Annotation.SnapAngle(s, p));
                            }
                            else pn.AddPoint(p);
                            break;
                    }
                    Refresh(_creating);
                    break;
            }
        }

        public void OnUp(Point p)
        {
            var d = _drag;
            _drag = Drag.None;
            if (d == Drag.Create && _creating != null)
            {
                if (_creating is PenAnnotation pn && pn.Points.Count == 1) pn.Points.Add(pn.Points[0] + new Vector(0.1, 0.1));
                if (_creating.IsEmpty)
                {
                    Remove(_creating);
                    _selected = null;
                    _pending = null;
                }
                else CommitChange();
                _creating = null;
            }
            else if (d == Drag.Move || d == Drag.Handle)
            {
                bool counterJustPlaced = _creating is CounterAnnotation;
                _creating = null;
                if (counterJustPlaced || (_pending != null && !SameAsPending())) CommitChange();
                else _pending = null;
            }
            RefreshHandles();
            StateChanged?.Invoke();
        }

        bool SameAsPending()
        {
            // cheap check: a pure click on an item without moving it shouldn't create an undo step
            if (_pending.Count != Items.Count) return false;
            for (int i = 0; i < Items.Count; i++)
                if (_pending[i].Bounds != Items[i].Bounds) return false;
            return true;
        }

        // ------------------------------------------------------------------ text editing

        void BeginTextEdit(TextAnnotation t, bool isNew)
        {
            EndTextEdit();
            _editing = t;
            _editingIsNew = isNew;
            _textBefore = t.Text;
            t.Editing = true;
            if (!isNew) BeginChange();

            var tb = new TextBox
            {
                Text = t.Text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Foreground = Brushes.Transparent,
                MinWidth = 2,
                Cursor = Cursors.IBeam,
            };
            tb.SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Ideal);
            ScrollViewer.SetHorizontalScrollBarVisibility(tb, ScrollBarVisibility.Hidden);
            ScrollViewer.SetVerticalScrollBarVisibility(tb, ScrollBarVisibility.Hidden);
            var tpl = new ControlTemplate(typeof(TextBox));
            var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            host.SetValue(ScrollViewer.PaddingProperty, new Thickness(0));
            host.SetValue(MarginProperty, new Thickness(0));
            tpl.VisualTree = host;
            tb.Template = tpl;
            _editor = tb;
            ApplyEditorStyle();

            tb.TextChanged += (_, _) =>
            {
                t.Text = tb.Text.Replace("\r\n", "\n");
                Refresh(t);
            };
            tb.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape || (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0))
                {
                    EndTextEdit();
                    e.Handled = true;
                }
            };
            tb.LostKeyboardFocus += (_, e) =>
            {
                // focus moving to the toolbar (e.g. picking a color) shouldn't end editing
                if (e.NewFocus is DependencyObject d && IsAncestorOf(d)) return;
            };

            _textCanvas.Children.Add(tb);
            Canvas.SetLeft(tb, t.Position.X);
            Canvas.SetTop(tb, t.Position.Y);
            tb.UpdateLayout();
            AlignEditor();
            tb.Focus();
            tb.CaretIndex = tb.Text.Length;
            Keyboard.Focus(tb);
            Refresh(t);
            RefreshHandles();
            StateChanged?.Invoke();
        }

        void ApplyEditorStyle()
        {
            if (_editor == null || _editing == null) return;
            _editor.FontFamily = KFonts.Family;
            _editor.FontWeight = FontWeights.Bold;
            _editor.FontSize = _editing.FontSize;
            var caret = _editing.Style == TextStyleKind.Pill ? Annotation.Contrast(_editing.Color) : _editing.Color;
            _editor.CaretBrush = new SolidColorBrush(caret);
            _editor.SelectionBrush = new SolidColorBrush(Accent);
            _editor.SelectionOpacity = 0.35;
            _editor.UpdateLayout();
            AlignEditor();
        }

        void AlignEditor()
        {
            if (_editor == null) return;
            var r = _editor.GetRectFromCharacterIndex(0);
            double ox = double.IsFinite(r.X) ? r.X : 0, oy = double.IsFinite(r.Y) ? r.Y : 0;
            Canvas.SetLeft(_editor, _editing.Position.X - ox);
            Canvas.SetTop(_editor, _editing.Position.Y - oy);
        }

        public void EndTextEdit()
        {
            if (_editor == null) return;
            var t = _editing;
            var tb = _editor;
            _editor = null;
            _editing = null;
            _textCanvas.Children.Remove(tb);
            t.Editing = false;
            if (t.IsEmpty)
            {
                Remove(t);
                if (_selected == t) _selected = null;
                if (!_editingIsNew && _pending != null) CommitChange(); else _pending = null;
            }
            else if (_editingIsNew)
            {
                // record the creation: snapshot without this text
                var before = Items.Where(a => a != t).Select(a => a.Clone()).ToList();
                _undo.Push(before);
                _redo.Clear();
                Refresh(t);
                ContentChanged?.Invoke();
            }
            else
            {
                Refresh(t);
                if (t.Text != _textBefore && _pending != null) CommitChange(); else _pending = null;
            }
            RefreshHandles();
            StateChanged?.Invoke();
            Keyboard.Focus(Window.GetWindow(this));
        }

        // ------------------------------------------------------------------ export

        /// <summary>
        /// Render all annotations into a premultiplied BGRA buffer covering <paramref name="cropPx"/>
        /// (pixel rect in surface space at <paramref name="scale"/>).
        /// </summary>
        public static byte[] RenderOverlay(IEnumerable<Annotation> items, RenderEnv env, Int32Rect cropPx, double scale)
        {
            var root = new ContainerVisual { Transform = new TranslateTransform(-cropPx.X / scale, -cropPx.Y / scale) };
            foreach (var a in Ordered(items))
            {
                if (a.IsEmpty) continue;
                var v = new DrawingVisual();
                if (a.HasShadow) v.Effect = Annotation.Shadow;
                using (var dc = v.RenderOpen()) a.Render(dc, env);
                root.Children.Add(v);
            }
            var host = new ContainerVisual();
            host.Children.Add(root);
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(cropPx.Width, cropPx.Height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(host);
            var px = new byte[cropPx.Width * cropPx.Height * 4];
            rtb.CopyPixels(px, cropPx.Width * 4, 0);
            return px;
        }

        public List<Annotation> CloneItems(Vector offset)
        {
            EndTextEdit();
            return Items.Where(a => !a.IsEmpty).Select(a => { var c = a.Clone(); c.Move(offset); return c; }).ToList();
        }
    }
}
