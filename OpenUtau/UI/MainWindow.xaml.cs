﻿using System;
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
using System.Windows.Shapes;
using System.Windows.Shell;

using WinInterop = System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Win32;

using OpenUtau.UI.Models;
using OpenUtau.UI.Controls;
using OpenUtau.Core;
using OpenUtau.Core.USTx;

namespace OpenUtau.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : BorderlessWindow
    {
        MidiWindow midiWindow;
        UProject uproject;
        TracksViewModel trackVM;

        public MainWindow()
        {
            InitializeComponent();

            this.Width = Properties.Settings.Default.MainWidth;
            this.Height = Properties.Settings.Default.MainHeight;
            this.WindowState = Properties.Settings.Default.MainMaximized ? WindowState.Maximized : WindowState.Normal;

            ThemeManager.LoadTheme(); // TODO : move to program entry point

            this.CloseButtonClicked += (o, e) => { CmdExit(); };
            CompositionTargetEx.FrameUpdating += RenderLoop;

            viewScaler.Max = UIConstants.TrackMaxHeight;
            viewScaler.Min = UIConstants.TrackMinHeight;
            viewScaler.Value = UIConstants.TrackDefaultHeight;

            trackVM = (TracksViewModel)this.Resources["tracksVM"];
            trackVM.TrackCanvas = this.trackCanvas;

            uproject = new UProject();
            trackVM.Project = uproject;
        }

        void RenderLoop(object sender, EventArgs e)
        {
            tickBackground.RenderIfUpdated();
            timelineBackground.RenderIfUpdated();
            trackBackground.RenderIfUpdated();
            trackVM.RedrawIfUpdated();
        }

        # region Timeline Canvas

        private void timelineCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            const double zoomSpeed = 0.0012;
            Point mousePos = e.GetPosition((UIElement)sender);
            double zoomCenter;
            if (trackVM.OffsetX == 0 && mousePos.X < 128) zoomCenter = 0;
            else zoomCenter = (trackVM.OffsetX + mousePos.X) / trackVM.QuarterWidth;
            trackVM.QuarterWidth *= 1 + e.Delta * zoomSpeed;
            trackVM.OffsetX = Math.Max(0, Math.Min(trackVM.TotalWidth, zoomCenter * trackVM.QuarterWidth - mousePos.X));
        }

        private void timelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            //ncModel.playPosMarkerOffset = ncModel.snapNoteOffset(e.GetPosition((UIElement)sender).X);
            //ncModel.updatePlayPosMarker();
            ((Canvas)sender).CaptureMouse();
        }

        private void timelineCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point mousePos = e.GetPosition((UIElement)sender);
            timelineCanvas_MouseMove_Helper(mousePos);
        }

        private void timelineCanvas_MouseMove_Helper(Point mousePos)
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed && Mouse.Captured == timelineCanvas)
            {
                //ncModel.playPosMarkerOffset = ncModel.snapNoteOffset(mousePos.X);
                //ncModel.updatePlayPosMarker();
            }
        }

        private void timelineCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ((Canvas)sender).ReleaseMouseCapture();
        }

        # endregion

        # region track canvas

        Rectangle selectionBox;
        Nullable<Point> selectionStart;

        bool _moveThumbnail = false;
        bool _resizeThumbnail = false;
        double _partMoveStartMouseQuater;
        int _partMoveStartTick;
        int _resizeMinDurTick;
        Point _mouseDownPos;
        PartThumbnail _hitThumbnail;
        List<PartThumbnail> selectedParts = new List<PartThumbnail>();

        private void trackCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_uiLocked) return;
            Point mousePos = e.GetPosition((UIElement)sender);

            var hit = VisualTreeHelper.HitTest(trackCanvas, mousePos).VisualHit;
            System.Diagnostics.Debug.WriteLine("Mouse hit " + hit.ToString());

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                selectionStart = new Point(trackVM.CanvasToQuarter(mousePos.X), trackVM.CanvasToTrack(mousePos.Y));
                if (Keyboard.IsKeyUp(Key.LeftShift) && Keyboard.IsKeyUp(Key.RightShift))
                {
                    trackVM.DeselectAll();
                }
                if (selectionBox == null)
                {
                    selectionBox = new Rectangle()
                    {
                        Stroke = Brushes.Black,
                        StrokeThickness = 2,
                        Fill = ThemeManager.getBarNumberBrush(),
                        Width = 0,
                        Height = 0,
                        Opacity = 0.5,
                        RadiusX = 8,
                        RadiusY = 8,
                        IsHitTestVisible = false
                    };
                    trackCanvas.Children.Add(selectionBox);
                    Canvas.SetZIndex(selectionBox, 1000);
                    selectionBox.Visibility = System.Windows.Visibility.Visible;
                }
                else
                {
                    selectionBox.Width = 0;
                    selectionBox.Height = 0;
                    Canvas.SetZIndex(selectionBox, 1000);
                    selectionBox.Visibility = System.Windows.Visibility.Visible;
                }
                Mouse.OverrideCursor = Cursors.Cross;
            }
            else if (hit is PartThumbnail)
            {
                PartThumbnail thumb = hit as PartThumbnail;
                _hitThumbnail = thumb;
                _mouseDownPos = mousePos;
                if (e.ClickCount == 2) // load part into midi window
                {
                    LockUI();
                    if (midiWindow == null) midiWindow = new MidiWindow(this);
                    midiWindow.LoadPart(thumb.Part, trackVM.Project);
                    midiWindow.Show();
                    midiWindow.Focus();
                    UnlockUI();
                }
                else if (mousePos.X > thumb.X + thumb.DisplayWidth - UIConstants.ResizeMargin) // resize
                {
                    _resizeThumbnail = true;
                    _resizeMinDurTick = trackVM.GetPartMinDurTick(_hitThumbnail.Part);
                    Mouse.OverrideCursor = Cursors.SizeWE;
                }
                else // move
                {
                    _moveThumbnail = true;
                    _partMoveStartMouseQuater = trackVM.CanvasToSnappedQuarter(mousePos.X);
                    _partMoveStartTick = thumb.Part.PosTick;
                    Mouse.OverrideCursor = Cursors.SizeAll;
                }
            }
            ((UIElement)sender).CaptureMouse();
        }

        private void trackCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _moveThumbnail = false;
            _resizeThumbnail = false;
            _hitThumbnail = null;
            selectionStart = null;
            if (selectionBox != null)
            {
                Canvas.SetZIndex(selectionBox, -100);
                selectionBox.Visibility = System.Windows.Visibility.Hidden;
            }
            trackVM.UpdateViewSize();
            ((UIElement)sender).ReleaseMouseCapture();
            Mouse.OverrideCursor = null;
        }

        private void trackCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point mousePos = e.GetPosition((UIElement)sender);
            trackCanvas_MouseMove_Helper(mousePos);
        }

        private void trackCanvas_MouseMove_Helper(Point mousePos)
        {

            if (selectionStart != null) // Selection
            {
                double bottom = trackVM.TrackToCanvas(Math.Max(trackVM.CanvasToTrack(mousePos.Y), (int)selectionStart.Value.Y) + 1);
                double top = trackVM.TrackToCanvas(Math.Min(trackVM.CanvasToTrack(mousePos.Y), (int)selectionStart.Value.Y));
                double left = Math.Min(mousePos.X, trackVM.QuarterToCanvas(selectionStart.Value.X));
                selectionBox.Width = Math.Abs(mousePos.X - trackVM.QuarterToCanvas(selectionStart.Value.X));
                selectionBox.Height = bottom - top;
                Canvas.SetLeft(selectionBox, left);
                Canvas.SetTop(selectionBox, top);
                //ncModel.trackPart.SelectTempInBox(
                //    ncModel.canvasToOffset(mousePos.X),
                //    selectionStart.Value.X,
                //    ncModel.snapNoteKey(mousePos.Y),
                //    selectionStart.Value.Y);
            }
            else if (_moveThumbnail) // move
            {
                if (selectedParts.Count == 0)
                {
                    _hitThumbnail.Part.TrackNo = trackVM.CanvasToTrack(mousePos.Y);
                    _hitThumbnail.Part.PosTick = Math.Max(0, _partMoveStartTick +
                        (int)(trackVM.Project.Resolution * (trackVM.CanvasToSnappedQuarter(mousePos.X) - _partMoveStartMouseQuater)));
                    trackVM.MarkUpdate();
                }
                else
                {
                }
            }
            else if (_resizeThumbnail) // resize
            {
                if (selectedParts.Count == 0)
                {
                    int newDurTick = (int)(trackVM.Project.Resolution * trackVM.CanvasRoundToSnappedQuarter(mousePos.X)) - _hitThumbnail.Part.PosTick;
                    if (newDurTick > _resizeMinDurTick)
                    {
                        _hitThumbnail.Part.DurTick = newDurTick;
                        trackVM.MarkUpdate();
                    }
                }
                else
                {
                }
            }
            else
            {
                HitTestResult result = VisualTreeHelper.HitTest(trackCanvas, mousePos);
                if (result == null) return;
                var hit = result.VisualHit;
                if (hit is PartThumbnail)
                {
                    PartThumbnail thumb = hit as PartThumbnail;
                    if (mousePos.X > thumb.X + thumb.DisplayWidth - UIConstants.ResizeMargin) Mouse.OverrideCursor = Cursors.SizeWE;
                    else Mouse.OverrideCursor = null;
                }
                else
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private void trackCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_uiLocked) return;
            ((UIElement)sender).CaptureMouse();
        }

        private void trackCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            trackVM.UpdateViewSize();
            ((UIElement)sender).ReleaseMouseCapture();
        }

        # endregion

        # region menu commands

        private void MenuOpen_Click(object sender, RoutedEventArgs e) { CmdOpenFileDialog(); }
        private void MenuExit_Click(object sender, RoutedEventArgs e) { CmdExit(); }

        private void MenuImportAidio_Click(object sender, RoutedEventArgs e)
        {
            LockUI();
            OpenFileDialog openFileDialog = new OpenFileDialog()
            {
                Filter = "Audio Files|*.*",
                Multiselect = false,
                CheckFileExists = true
            };
            if (openFileDialog.ShowDialog() == true) CmdImportAudio(openFileDialog.FileName);
            UnlockUI();
        }

        private void Menu_OpenMidiEditor(object sender, RoutedEventArgs e)
        {
            if (midiWindow == null) midiWindow = new MidiWindow(this);
            midiWindow.Show();
            midiWindow.Focus();
        }

        # endregion

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (_uiLocked) return;
            if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.F4) CmdExit();
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) CmdOpenFileDialog();
        }

        # region application commmands

        private void CmdOpenFileDialog()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog()
            {
                Filter = "Project Files|*.ustx; *.vsqx; *.ust|All Files|*.*",
                Multiselect = true,
                CheckFileExists = true
            };
            if (openFileDialog.ShowDialog() == true) CmdOpenFile(openFileDialog.FileNames);
        }

        private void CmdOpenFile(string[] files)
        {
            if (midiWindow != null) midiWindow.UnloadPart();
            LockUI();

            if (files.Length == 1)
            {
                uproject = OpenUtau.Core.Formats.Formats.LoadProject(files[0]);
            }
            else if (files.Length > 1)
            {
                uproject = OpenUtau.Core.Formats.Ust.Load(files);
            }

            if (uproject != null)
            {
                trackVM.LoadProject(uproject);
                Title = trackVM.Title;
            }

            UnlockUI();
        }

        private void CmdImportAudio(string file)
        {
            UWave uwave = OpenUtau.Core.Formats.Sound.Load(file);
            if (uwave != null)
            {
                uproject.Tracks.Add(new UTrack());
                uwave.TrackNo = uproject.Tracks.Count - 1;
                uwave.PosTick = 0;
                uwave.DurTick = (int)Math.Ceiling(MusicMath.MinutesToTick(uwave.Stream.TotalTime.TotalMinutes, uproject));
                System.Diagnostics.Debug.WriteLine("{0} {1} {2} {3} {4}", uwave.TrackNo, uwave.PosTick, uwave.DurTick, uwave.Name, uwave.ToString());
                uproject.Parts.Add(uwave);
                trackVM.AddPart(uwave);
            }
        }

        private void CmdExit()
        {
            Properties.Settings.Default.MainMaximized = this.WindowState == System.Windows.WindowState.Maximized;
            Properties.Settings.Default.Save();
            Application.Current.Shutdown();
        }

        # endregion

        private void navigateDrag_NavDrag(object sender, EventArgs e)
        {
            trackVM.OffsetX += ((NavDragEventArgs)e).X * trackVM.SmallChangeX;
            trackVM.OffsetY += ((NavDragEventArgs)e).Y * trackVM.SmallChangeY * 0.2;
            trackVM.MarkUpdate();
        }

        public void UpdatePartThumbnail(UPart part)
        {
            trackVM.UpdatePartThumbnail(part);
        }

        private void trackCanvas_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Copy;
        }

        private void trackCanvas_Drop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            CmdOpenFile(files);
        }

        bool _uiLocked = false;
        private void LockUI() { _uiLocked = true; Mouse.OverrideCursor = Cursors.AppStarting; }
        private void UnlockUI() { _uiLocked = false; Mouse.OverrideCursor = null; }

    }
}