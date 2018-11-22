﻿#region License & Metadata

// The MIT License (MIT)
// 
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
// 
// 
// Created On:   2018/06/11 14:33
// Modified On:  2018/11/20 20:05
// Modified By:  Alexis

#endregion




using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Patagames.Pdf;
using Patagames.Pdf.Enums;
using Patagames.Pdf.Net;
using Patagames.Pdf.Net.Controls.Wpf;
using SuperMemoAssistant.Plugins.PDF.Extensions;

namespace SuperMemoAssistant.Plugins.PDF.Viewer
{
  public partial class IPDFViewer
  {
    #region Constants & Statics

    protected const float TextSelectionSmoothTolerence = 6.0f;

    #endregion




    #region Properties & Fields - Non-Public

    protected Brush                          ImageHighlightFillHatchedBrush { get; } = CreateHatchedBrush();
    protected (PdfImageObject obj, int page) SelectedImage                  { get; set; }

    #endregion




    #region Methods Impl

    //
    // Tools

    /// <inheritdoc />
    protected override void ProcessMouseDownForSelectTextTool(Point pagePoint,
                                                              int   pageIndex)
    {
      var keyMod = GetKeyboardModifiers();

      if ((keyMod & KeyboardModifiers.ShiftKey) == KeyboardModifiers.ShiftKey)
        ExtendSelection(pagePoint,
                        pageIndex,
                        (keyMod & KeyboardModifiers.ControlKey) == KeyboardModifiers.ControlKey);

      else
        base.ProcessMouseDownForSelectTextTool(pagePoint,
                                               pageIndex);
    }

    protected override IEnumerable<Int32Rect> NormalizeRects(IEnumerable<FS_RECTF> rects,
                                                             int                   pageIndex)
    {
      rects = SmoothAlongY(rects.ToList());

      return rects.Select(r => PageToDeviceRect(r,
                                                pageIndex));
    }

    #endregion




    #region Methods

    protected bool OnMouseDownProcessSelection(MouseButtonEventArgs e,
                                               int                  pageIndex,
                                               Point                pagePoint)
    {
      bool handled    = false;
      bool invalidate = false;

      if (SelectInfo.StartPage >= 0)
      {
        DeselectText();
        invalidate = true;
      }

      if (SelectedImage.obj?.BoundingBox.Contains((int)pagePoint.X,
                                                  (int)pagePoint.Y) == false)
      {
        SelectedImage = (null, -1);
        invalidate    = true;
      }

      if (e.LeftButton == MouseButtonState.Pressed)
      {
        if (pageIndex >= 0)
        {
          var imgObj = Document.Pages[pageIndex].PageObjects
                               .FirstOrDefault(
                                 o => o.ObjectType == PageObjectTypes.PDFPAGE_IMAGE
                                   && o.BoundingBox.Contains((int)pagePoint.X,
                                                             (int)pagePoint.Y));

          if (imgObj != null)
          {
            SelectedImage = (imgObj as PdfImageObject, pageIndex);
            invalidate    = true;
            handled       = true;
          }
        }
      }

      if (invalidate)
        InvalidateVisual();


      return handled;
    }

    protected IEnumerable<FS_RECTF> SmoothAlongY(List<FS_RECTF> rects)
    {
      rects.Sort(new FS_RECTFYComparer());

      for (int i = 0; i < rects.Count; i++)
      {
        var curRect = rects[i];

        for (int j = i + 1;
             j < rects.Count && curRect.IsAdjacentAlongYWith(rects[j],
                                                             TextSelectionSmoothTolerence);
             j++)
          if (curRect.IsAlongsideXWith(rects[j],
                                       TextSelectionSmoothTolerence))
          {
            var itRect = rects[j];

            curRect.top = Math.Max(itRect.bottom,
                                   curRect.top);
            itRect.bottom = curRect.top;

            rects[j] = itRect;
          }

        rects[i] = curRect;
      }

      return rects;
    }

    private void ExtendSelection(Point pagePoint,
                                 int   pageIdx,
                                 bool  additive)
    {
      var selInfo = SelectInfo;

      if (selInfo.StartPage >= 0 && selInfo.EndPage >= 0)
      {
        int charIdx = Document.Pages[pageIdx].Text.GetCharIndexAtPos((float)pagePoint.X,
                                                                     (float)pagePoint.Y,
                                                                     10.0f,
                                                                     10.0f);

        if (charIdx >= 0)
        {
          int startPage = additive
            ? Math.Min(_selectInfo.StartPage,
                       pageIdx)
            : _selectInfo.StartPage;
          int endPage = additive
            ? Math.Max(_selectInfo.EndPage,
                       pageIdx)
            : pageIdx;

          int startIdx = additive
            ? Math.Min(_selectInfo.StartIndex,
                       charIdx)
            : _selectInfo.StartIndex;
          int endIdx = additive
            ? Math.Max(_selectInfo.EndIndex,
                       charIdx)
            : charIdx;

          _selectInfo = new SelectInfo()
          {
            StartPage  = startPage,
            EndPage    = endPage,
            StartIndex = startIdx,
            EndIndex   = endIdx
          };
          _isShowSelection = true;

          InvalidateVisual();
        }
      }
    }

    protected void CopySelectionToClipboard()
    {
      if (string.IsNullOrWhiteSpace(SelectedText) == false)
        Clipboard.SetText(SelectedText);
    }

    #endregion
  }
}