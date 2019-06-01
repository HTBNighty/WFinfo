﻿Imports System.Text.RegularExpressions
Imports Tesseract

Public Class OCR2
    ' Pixel measurements for reward screen @ 1920 x 1080 with 100% scale
    Public Const pixRwrdWid As Integer = 968
    Public Const pixRwrdHei As Integer = 235
    Public Const pixRwrdYDisp As Integer = 185
    Public Const pixRwrdLineHei As Integer = 22
    Public Const pixRwrdLineWid As Integer = 240

    ' Pixel measurement for rarity bars for player count
    '   Width is same as pixRwrdWid
    ' Public Const pixRareWid As Integer = pixRwrdWid
    '   Height is always 1px
    ' Public Const pixRareHei As Integer = 1
    '   Box is centered horizontally
    ' Public Const pixRareXDisp As Integer = ???
    Public Const pixRareYDisp As Integer = 196

    ' Pixel measurements for detecting reward screen
    Public Const pixFissWid As Integer = 354
    Public Const pixFissHei As Integer = 45
    Public Const pixFissXDisp As Integer = 285
    Public Const pixFissYDisp As Integer = 43

    ' Colors for "VOIDFISSURE/REWARDS"
    Public Const pixProfWid As Integer = 39
    ' Height is always 1px
    ' Public Const pixProfHei As Integer = 1
    Public Const pixProfXDisp As Integer = 98
    Public Const pixProfYDisp As Integer = 86
    Public FissClr1 As Color = Color.FromArgb(189, 168, 101)    ' default
    Public FissClr2 As Color = Color.FromArgb(153, 31, 35)      ' stalker
    Public FissClr3 As Color = Color.FromArgb(238, 193, 105)    ' baruk
    Public FissClr4 As Color = Color.FromArgb(35, 201, 245)     ' corpus
    Public FissClr5 As Color = Color.FromArgb(57, 105, 192)     ' fortuna
    Public FissClr6 As Color = Color.FromArgb(255, 189, 102)    ' grineer
    Public FissClr7 As Color = Color.FromArgb(36, 184, 242)     ' lotus
    Public FissClr8 As Color = Color.FromArgb(140, 38, 92)      ' nidus
    Public FissClr9 As Color = Color.FromArgb(20, 41, 29)       ' orokin
    Public FissClr10 As Color = Color.FromArgb(9, 78, 106)      ' tenno

    Public rarity As New List(Of Color) From {Color.FromArgb(182, 105, 77), Color.FromArgb(119, 119, 119), Color.FromArgb(163, 143, 70)}
    Public fissColors As Color() = {FissClr1, FissClr2, FissClr3, FissClr4, FissClr5, FissClr6, FissClr7, FissClr8, FissClr9, FissClr10}

    ' Warframe window stats
    '   Warframe process
    Public WF_Proc As Process = Nothing
    '   Warframe window style (Fullscreen, Windowed, Borderless)
    Public currStyle As WindowStyle

    '   Warframe window bounds & points
    Public window As Rectangle = Nothing
    Public center As Point = Nothing

    ' Scaling adjustments
    Public dpiScaling As Double = -1.0
    Public uiScaling As Double = -1.0
    Public totalScaling As Double = -1.0

    ' Tesseract engines for each reward
    ' 0 for relic window checks or any system checks
    ' 1-4 for players 1 to 4
    Public engine(5) As TesseractEngine

    Public Enum WindowStyle
        FULLSCREEN
        BORDERLESS
        WINDOWED
    End Enum

    Public Enum DeviceCap
        VERTRES = 10
        DESKTOPVERTRES = 117
    End Enum

    <StructLayout(LayoutKind.Sequential)> Public Structure RECT
        Dim Left As Integer
        Dim Top As Integer
        Dim Right As Integer
        Dim Bottom As Integer
    End Structure

    <DllImport("user32.dll")>
    Public Shared Function GetWindowRect(ByVal hWnd As HandleRef, ByRef lpRect As RECT) As Boolean
    End Function

    <DllImport("User32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Shared Function GetWindowLong(hWnd As IntPtr, nIndex As Int16) As Int32
    End Function

    <DllImport("dwmapi.dll", PreserveSig:=False)>
    Public Shared Sub DwmEnableComposition(bEnable As Boolean)
    End Sub

    <DllImport("gdi32.dll")>
    Public Shared Function GetDeviceCaps(hdc As IntPtr, nIndex As Integer) As Integer
    End Function

    Public Sub New()
        For i As Integer = 0 To 4
            engine(i) = New TesseractEngine("", "eng")
            engine(i).DefaultPageSegMode = PageSegMode.SingleLine
            engine(i).SetVariable("load_system_dawg", False)
            engine(i).SetVariable("user_words_suffix", "prime-words")
            engine(i).SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZ/")
        Next
    End Sub

    '----------------------------------------------------------------------
    ' Utility Functions
    '----------------------------------------------------------------------

    Public Overridable Function cleanImage(image As Bitmap) As Bitmap
        Dim uiColor As Color = Nothing
        Dim clr As Color = Nothing
        For i As Integer = 0 To image.Width - 1
            For j As Integer = 0 To image.Height - 1 'Loops through the whole image starting at the first line going down
                clr = image.GetPixel(i, j)
                If clr = uiColor Then 'if found color is the UI color then make it black
                    image.SetPixel(i, j, Color.Black)
                    Continue For

                ElseIf uiColor = Nothing Then 'if ui color is not set yet see if the current pixle is part of the ui colors
                    For Each color In fissColors
                        If clr = color Then 'if the currecnt pixle is part of the ui colors set it to black and set ui colors
                            image.SetPixel(i, j, Color.Black)
                            uiColor = color
                            Continue For
                        End If
                    Next

                End If
                image.SetPixel(i, j, Color.White) 'default to setting the pixle to white
            Next
        Next
        Return image
    End Function

    Public Overridable Function GetDPIScaling() As Double
        dpiScaling = 1

        Using form As New Form()
            Using g As Graphics = form.CreateGraphics()
                If g.DpiX <> 96 Then
                    Main.addLog("FOUND DPI: g.DpiX")
                    dpiScaling = g.DpiX / 96
                    Return dpiScaling
                ElseIf g.DpiY <> 96 Then
                    Main.addLog("FOUND DPI: g.DpiY")
                    dpiScaling = g.DpiY / 96
                    Return dpiScaling
                End If
            End Using
        End Using

        Using g As Graphics = Graphics.FromHwnd(IntPtr.Zero)
            Dim desktop As IntPtr = g.GetHdc()
            dpiScaling = GetDeviceCaps(desktop, DeviceCap.DESKTOPVERTRES) / GetDeviceCaps(desktop, DeviceCap.VERTRES)
            If dpiScaling <> 1 Then
                Main.addLog("FOUND DPI: VERTRES")
            End If
        End Using

        totalScaling = dpiScaling * uiScaling
        Return dpiScaling
    End Function

    Public Overridable Function GetUIScaling() As Double
        uiScaling = My.Settings.Scaling / 100
        '     All values are based on 1920x1080
        If window.Width / window.Height > 16 / 9 Then
            uiScaling *= window.Height / 1080
        Else
            uiScaling *= window.Width / 1920
        End If
        totalScaling = dpiScaling * uiScaling
        Return uiScaling
    End Function

    Public Overridable Function GetWFProc() As Boolean
        For Each p As Process In Process.GetProcesses
            If p.ProcessName.Contains("Warframe") Then
                If WF_Proc Is Nothing OrElse p.Handle <> WF_Proc.Handle Then
                    WF_Proc = p
                    UpdateCenter()
                End If
                Return True
            End If
        Next
        Return False
    End Function

    Public Overridable Function IsWFActive() As Boolean
        If WF_Proc Is Nothing Then
            GetWFProc()
        ElseIf WF_Proc.HasExited Then
            WF_Proc = Nothing
        End If
        Return WF_Proc IsNot Nothing
    End Function

    Public Overridable Sub UpdateCenter()
        Dim hr As New HandleRef(WF_Proc, WF_Proc.MainWindowHandle)
        Dim tempRect As New RECT
        GetWindowRect(hr, tempRect)

        window = New Rectangle(tempRect.Left, tempRect.Top, tempRect.Right - tempRect.Left, tempRect.Bottom - tempRect.Top)
        Main.addLog("WINDOW AREA: " & window.ToString())

        Dim GWL_STYLE As Int32 = -16
        Dim WS_THICKFRAME As Long = 262144
        Dim WS_MAXIMIZE As Long = 16777216
        Dim WS_POPUP As Long = 2147483648
        Dim styles As Long = GetWindowLong(WF_Proc.MainWindowHandle, GWL_STYLE)
        If (styles And WS_THICKFRAME) <> 0 Then
            window = New Rectangle(window.Left + 8, window.Top + 30, window.Width - 16, window.Height - 38)
            Main.addLog("WINDOWED ADJUSTMENT: " & window.ToString())
            currStyle = WindowStyle.WINDOWED
            DwmEnableComposition(True)
        ElseIf (styles And WS_POPUP) <> 0 Then
            currStyle = WindowStyle.BORDERLESS
            DwmEnableComposition(True)
        Else
            currStyle = WindowStyle.FULLSCREEN
            DwmEnableComposition(False)
        End If

        If window.Width <= 0 Or window.Height <= 0 Then
            WF_Proc = Nothing
            window = Nothing
        Else
            ' Get DPI Scaling
            GetDPIScaling()

            ' Get Window Points
            Dim horz_center As Integer = window.Left + (window.Width / 2)
            Dim vert_center As Integer = window.Top + (window.Height / 2)
            center = New Point(dpiScaling * horz_center, dpiScaling * vert_center)

            Main.addLog("UPDATED CENTER COORS: " & center.ToString())
        End If
    End Sub

    Public Overridable Function DefaultParseText(bmp As Bitmap, plyr As Integer) As String
        Using bmp
            Using page As Page = engine(plyr).Process(bmp)
                Return page.GetText().Trim
            End Using
        End Using
    End Function

    '_____________________________________________________
    '
    '    BEGIN RELIC REWARDS STUFF
    '_____________________________________________________

    Public Overridable Function ColorThreshold(test As Color, thresh As Color) As Boolean
        Dim threshold As Integer = 10
        Return (Math.Abs(CInt(test.R) - thresh.R) < threshold) AndAlso (Math.Abs(CInt(test.G) - thresh.G) < threshold) AndAlso (Math.Abs(CInt(test.B) - thresh.B) < threshold)
    End Function

    Public Overridable Function IsRelicWindow() As Boolean
        If Not IsWFActive() Then
            Return False
        End If

        GetUIScaling()

        Dim bnds As New Rectangle(window.Left + pixFissXDisp * totalScaling, window.Top + pixFissYDisp * totalScaling,
                                  pixFissWid * totalScaling, pixFissHei * totalScaling)

        Using bmp As New Bitmap(bnds.Width + 40, bnds.Height + 40)
            Using graph As Graphics = Graphics.FromImage(bmp)
                graph.CopyFromScreen(pixProfXDisp * totalScaling, pixProfYDisp * totalScaling, 0, 0, New Size(pixProfWid * totalScaling, 1), CopyPixelOperation.SourceCopy)

                graph.CopyFromScreen(bnds.X, bnds.Y, 20, 20, New Size(bnds.Width, bnds.Height), CopyPixelOperation.SourceCopy)
            End Using

            Dim clr As Color = Nothing
            Dim R, G, B As Integer


            For i As Integer = 0 To pixProfWid * totalScaling - 1
                clr = bmp.GetPixel(i, 0)
                R += clr.R
                G += clr.G
                B += clr.B
                bmp.SetPixel(i, 0, Color.White)
            Next

            R /= pixProfWid * totalScaling
            G /= pixProfWid * totalScaling
            B /= pixProfWid * totalScaling

            FissClr1 = Color.FromArgb(R, G, B)

            Dim found As Boolean = False
            For i As Integer = 0 To bmp.Width - 1
                For j As Integer = 0 To bmp.Height - 1
                    clr = bmp.GetPixel(i, j)
                    If ColorThreshold(clr, FissClr1) Then
                        found = True
                        bmp.SetPixel(i, j, Color.Black)
                    Else
                        bmp.SetPixel(i, j, Color.White)
                    End If
                Next
            Next
            If Debug Then
                bmp.Save(appData & "\WFInfo\debug\FISS_CHECK-" & My.Settings.EtcCount.ToString() & ".png")
                Main.addLog("SAVING SCREENSHOT: " & appData & "\WFInfo\debug\FISS_CHECK-" & My.Settings.EtcCount.ToString() & ".png")
                My.Settings.EtcCount += 1
            End If

            If Not found Then
                Return False
            End If

            Dim ret As String = DefaultParseText(bmp, 0)
            ' Finds: VUIIJ FISSUHE/HEWAHDS
            Console.WriteLine("---" & ret & "---")

            If LevDist(ret, "VUIIJ FISSUHE") < 4 Then
                Console.WriteLine("REWARD WINDOW FOUND")
                Return True
            End If
        End Using
        Return False
    End Function

    ' Public Overridable Function Screenshot(width As Integer, height As Integer, heightPosition As Integer) As Bitmap
    Public Overridable Function GetRelicWindow() As Bitmap

        GetDPIScaling()
        GetUIScaling()

        Dim width As Integer = pixRwrdWid * totalScaling
        Dim height As Integer = pixRwrdHei * totalScaling

        Dim ss_area As New Rectangle(center.X - width / 2,
                                     center.Y - pixRwrdYDisp * totalScaling,
                                     width,
                                     height)

        Dim vf_area As New Rectangle(window.Left + pixFissXDisp * totalScaling, window.Top + pixFissYDisp * totalScaling,
                                  pixFissWid * totalScaling, pixFissHei * totalScaling)

        Main.addLog("TAKING SCREENSHOT:" & vbCrLf & "DPI SCALE: " & dpiScaling & vbCrLf & "SS REGION: " & ss_area.ToString())
        Dim ret As New Bitmap(width, height)
        If Debug Then 'screenshot the whole screen
            Dim debugRet As New Bitmap(CInt(Screen.PrimaryScreen.Bounds.Width * dpiScaling), CInt(Screen.PrimaryScreen.Bounds.Height * dpiScaling))
            Using graph As Graphics = Graphics.FromImage(debugRet)
                Dim screenSize As New Size(Screen.PrimaryScreen.Bounds.Width * dpiScaling, Screen.PrimaryScreen.Bounds.Height * dpiScaling)
                graph.CopyFromScreen(Screen.PrimaryScreen.Bounds.X, Screen.PrimaryScreen.Bounds.Y, 0, 0, screenSize, CopyPixelOperation.SourceCopy)
                Dim print As String =
                        "Tried looking at " & ss_area.ToString & vbCrLf &
                        "Screen resolution: " & Screen.PrimaryScreen.Bounds.Size.ToString & vbCrLf &
                        "Screen center: " & center.ToString & vbCrLf &
                        "Screen bounds: " & window.ToString & vbCrLf &
                        "UI scaling: " & uiScaling & vbTab & vbTab & " Windows scaling: " & dpiScaling
                Dim font As New Font("Tahoma", (Screen.PrimaryScreen.Bounds.Height / 120.0))
                Dim printBounds As SizeF = graph.MeasureString(print, font, graph.MeasureString(print, font).Width)

                Dim textbox = New Rectangle(center.X, ss_area.Bottom + 3, printBounds.Width, printBounds.Height)

                Dim print2 As String =
                        "Tried looking at " & vf_area.ToString & vbCrLf &
                        "Screen top-left: " & (New Point(window.X, window.Y)).ToString & vbCrLf &
                        "UI scaling: " & uiScaling & vbTab & vbTab & " Windows scaling: " & dpiScaling

                Dim printBounds2 As SizeF = graph.MeasureString(print2, font, graph.MeasureString(print2, font).Width)
                Dim textbox2 = New Rectangle((vf_area.Left + vf_area.Right) / 2, vf_area.Bottom + 3, printBounds2.Width, printBounds2.Height)

                graph.FillEllipse(Brushes.Red, center.X - 3, center.Y - 3, 3, 3)    'Dot centered at where it thinks the center of warframe is
                graph.DrawRectangle(New Pen(Brushes.Red), ss_area)                  'The area that it tried to read from
                graph.FillRectangle(Brushes.Black, textbox)                         'Black background for text box
                graph.DrawString(print, font, Brushes.Red, textbox)                 'Debug text ontop of screenshot
                graph.DrawRectangle(New Pen(Brushes.Red), vf_area)                  'The area that it tried to read from
                graph.FillRectangle(Brushes.Black, textbox2)                         'Black background for text box
                graph.DrawString(print2, font, Brushes.Red, textbox2)                 'Debug text ontop of screenshot

                ' TODO: Add text box and rectangle for relic check at top left

                debugRet.Save(appData & "\WFInfo\debug\SSFULL-" & My.Settings.SSCount.ToString() & ".png")
                Main.addLog("SAVING SCREENSHOT: " & appData & "\WFInfo\debug\SSFULL-" & My.Settings.SSCount.ToString() & ".png")
                My.Settings.SSCount += 1
            End Using
        End If

        Using graph As Graphics = Graphics.FromImage(ret)
            graph.CopyFromScreen(ss_area.X, ss_area.Y, 0, 0, New Size(ss_area.Width, ss_area.Height), CopyPixelOperation.SourceCopy)
        End Using
        Return ret
    End Function

    Public Overridable Function GetPlayers() As Integer
        If Not IsWFActive() Then
            Return False
        End If

        GetUIScaling()

        Dim count As Integer = 0
        Dim bnds As New Rectangle(center.X - pixRwrdWid * totalScaling / 2, center.Y - pixRareYDisp * totalScaling,
                                  pixRwrdWid * totalScaling, 1)

        Using bmp As New Bitmap(bnds.Width, bnds.Height)
            Using graph As Graphics = Graphics.FromImage(bmp)

                graph.CopyFromScreen(bnds.X, bnds.Y, 0, 0, New Size(bnds.Width, bnds.Height), CopyPixelOperation.SourceCopy)
            End Using

            Dim clr As Color = Nothing
            Dim thresh As Color = Nothing
            Dim x As Integer = 0
            Dim scanWid As Integer = 30 * totalScaling
            For i As Integer = 0 To 7
                x = (i + 0.5) * bmp.Width / 8 - scanWid / 2
                clr = bmp.GetPixel(x, 0)
                If ColorThreshold(clr, rarity(0)) Then
                    thresh = rarity(0)
                ElseIf ColorThreshold(clr, rarity(1)) Then
                    thresh = rarity(1)
                ElseIf ColorThreshold(clr, rarity(2)) Then
                    thresh = rarity(2)
                Else
                    Continue For
                End If

                Dim success As Boolean = True
                For j As Integer = 1 To scanWid - 1
                    clr = bmp.GetPixel(x + j, 0)
                    If ColorThreshold(clr, thresh) Then
                        bmp.SetPixel(x + j, 0, Color.Black)
                    Else
                        success = False
                        bmp.SetPixel(x + j, 0, Color.White)
                        Exit For
                    End If
                Next
                If success Then
                    count += 1
                ElseIf count > 0 Then
                    Exit For
                End If
            Next
            If count Mod 2 = 0 Then
                count /= 2
            Else
                Main.addLog("ERROR WITH PLAYER CALCULATION: FOUND " & count / 2 & " SEGMENTS")
                bmp.Save(appData & "\WFInfo\debug\RareBar-" & My.Settings.EtcCount.ToString() & ".png")
                Main.addLog("SAVING SCREENSHOT: " & appData & "\WFInfo\debug\RareBar-" & My.Settings.EtcCount.ToString() & ".png")
                My.Settings.EtcCount += 1

                count = 0
            End If
        End Using

        Return count
    End Function


    Public GetPartText_timer As Long = 0
    Public Overridable Function GetPartText(screen As Bitmap, plyr_count As Integer, plyr As Integer, Optional multiLine As Boolean = False) As String
        GetPartText_timer = clock.Elapsed.TotalMilliseconds
        Return "N/A"
    End Function

End Class