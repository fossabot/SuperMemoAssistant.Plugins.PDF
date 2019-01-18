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
// Created On:   2018/12/10 14:46
// Modified On:  2018/12/23 17:37
// Modified By:  Alexis

#endregion




using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Patagames.Pdf.Net;
using Patagames.Pdf.Net.Controls.Wpf;
using SuperMemoAssistant.Extensions;
using SuperMemoAssistant.Interop.SuperMemo.Content.Contents;
using SuperMemoAssistant.Interop.SuperMemo.Elements;
using SuperMemoAssistant.Interop.SuperMemo.Elements.Builders;
using SuperMemoAssistant.Interop.SuperMemo.Elements.Models;
using SuperMemoAssistant.Plugins.PDF.Models;
using SuperMemoAssistant.Services;
using SuperMemoAssistant.Sys.Drawing;

namespace SuperMemoAssistant.Plugins.PDF.PDF.Viewer
{
  public partial class IPDFViewer
  {
    #region Methods

    protected bool CreateSMExtract()
    {
      bool ret = false;

      bool txtExtract  = false;
      var  imgExtracts = new List<PDFImageExtract>();
      var  contents    = new List<ContentBase>();

      var selImages = SelectedImages;
      var selImageAreas = SelectedAreas.Where(a => a.Type == PDFAreaSelection.AreaType.Normal);
      var selTextAreas = SelectedAreas.Where(a => a.Type == PDFAreaSelection.AreaType.Ocr).ToList();

      // Image extract
      foreach (var selImage in selImages)
      {
        var imgExtract = new PDFImageExtract
        {
          BoundingBox = selImage.BoundingBox,
          ObjectIndex = selImage.ObjectIndex,
          PageIndex   = selImage.PageIndex,
        };
        var imgObj           = (PdfImageObject)Document.Pages[selImage.PageIndex].PageObjects[selImage.ObjectIndex];
        var imgRegistryTitle = TitleOrFileName + $": {selImage}";

        var content = CreateImageContent(imgObj.Bitmap.Image,
                                         imgRegistryTitle);

        if (content != null)
        {
          imgExtracts.Add(imgExtract);
          contents.Add(content);
        }
      }

      // Area extract
      foreach (var selArea in selImageAreas)
      {
        var imgExtract = new PDFImageExtract
        {
          BoundingBox = selArea.Normalized(),
          ObjectIndex = -1,
          PageIndex   = selArea.PageIndex,
        };

        var (lt, rb) = selArea.NormalizedPoints();
        var img = RenderArea(imgExtract.PageIndex,
                             lt,
                             rb);

        var imgRegistryTitle = TitleOrFileName + $": {selArea}";

        var content = CreateImageContent(img,
                                         imgRegistryTitle);

        if (content != null)
        {
          imgExtracts.Add(imgExtract);
          contents.Add(content);
        }
      }

      // Text extract
      if (string.IsNullOrWhiteSpace(SelectedText) == false)
      {
        string text = GetSelectedTextHtml();

        contents.Add(new TextContent(true,
                                                    text));
        txtExtract = true;
      }

      if (selTextAreas.Any())
      {
        var text = string.Join("\r\n<br/>[...] ",
                               selTextAreas.Select(a => a.OcrText));
        contents.Add(new TextContent(true,
                                                    text));
      }

      // Generate extract
      if (contents.Count > 0)
      {
        Save(false);

        ret = Svc.SMA.Registry.Element.Add(
          new ElementBuilder(ElementType.Topic,
                             contents.ToArray())
            .WithParent(Svc.SMA.Registry.Element[PDFElement.ElementId])
            .WithPriority(Config.SMExtractPriority)
            .WithReference(r => PDFElement.ConfigureReferences(r))
            .DoNotDisplay()
        );

        if (ret)
        {
          foreach (var imgExtract in imgExtracts)
          {
            PDFElement.SMImgExtracts.Add(imgExtract);
            AddImgExtractHighlight(imgExtract.PageIndex,
                                   imgExtract.BoundingBox);
          }

          if (txtExtract)
          {
            foreach (var selInfo in SelectInfos)
            {
              PDFElement.SMExtracts.Add(selInfo);
              AddSMExtractHighlight(selInfo);
            }
          }

          Save(false);
          DeselectAll();
        }
      }

      return ret;
    }

    protected ContentBase CreateImageContent(Image           image,
                                                         string          title)
    {
      if (image == null)
        return null;

      int imgRegistryId = Svc.SMA.Registry.Image.AddMember(
        new ImageWrapper(image),
        title
      );

      if (imgRegistryId <= 0)
        return null;

      return new ImageContent(imgRegistryId,
                                             Config.ImageStretchType);
    }

    protected bool CreateImageExtract(PDFImageExtract extract,
                                      Image           image,
                                      string          title)
    {
      int imgRegistryId = Svc.SMA.Registry.Image.AddMember(
        new ImageWrapper(image),
        title
      );

      if (imgRegistryId <= 0)
        return false;

      bool ret = Svc.SMA.Registry.Element.Add(
        new ElementBuilder(ElementType.Topic,
                           new ImageContent(imgRegistryId))
          .WithParent(Svc.SMA.Registry.Element[PDFElement.ElementId])
          .WithReference(r =>
                           PDFElement.ConfigureReferences(r)
                                     .WithComment(title))
          .DoNotDisplay()
      );

      if (ret)
      {
        PDFElement.SMImgExtracts.Add(extract);
        Save(false);

        AddImgExtractHighlight(extract.PageIndex,
                               extract.BoundingBox);
      }

      return ret;
    }

    // PDF Extracts

    protected bool CreatePDFExtract()
    {
      bool ret = false;

      if (SelectedPages != null)
      {
        var selPages = SelectedPages.Normalized;
        var selInfo = new SelectInfo
        {
          StartPage  = selPages.StartPage,
          StartIndex = 0,
          EndPage    = selPages.EndPage,
          EndIndex   = Document.Pages[selPages.EndPage].Text.CountChars
        };

        if (IsPageInClientRect(selPages.EndPage) == false)
          Document.Pages[selPages.EndPage].Dispose();

        ret = CreatePDFExtract(selInfo);

        if (ret)
          DeselectPages();
      }

      else if (string.IsNullOrWhiteSpace(SelectedText) == false)
      {
        var selInfo = SelectInfo;

        ret = CreatePDFExtract(selInfo);

        if (ret)
          DeselectText();
      }

      return ret;
    }

    protected bool CreatePDFExtract(SelectInfo selInfo,
                                    string     title = null)
    {
      Save(false);

      var extractPagePt = GetCharPointEx(selInfo.StartPage,
                                         selInfo.StartIndex);

      bool ret = PDFElement.Create(PDFElement.BinaryMember,
                                   selInfo.StartPage,
                                   selInfo.EndPage,
                                   selInfo.StartIndex,
                                   selInfo.EndIndex,
                                   PDFElement.ElementId,
                                   selInfo.StartPage,
                                   extractPagePt,
                                   PDFElement.ViewMode,
                                   PDFElement.PageMargin,
                                   PDFElement.Zoom,
                                   false,
                                   title) == PDFElement.CreationResult.Ok;

      if (ret)
      {
        PDFElement.PDFExtracts.Add(selInfo);
        Save(false);

        AddPDFExtractHighlight(selInfo);
      }

      return ret;
    }

    protected bool CreateIgnoreHighlight()
    {
      if (IsTextSelectionValid(out _) == false)
        return false;

      foreach (var selInfo in SelectInfos)
      {
        PDFElement.IgnoreHighlights.Add(selInfo);
        AddIgnoreHighlight(selInfo);
      }

      DeselectText();

      return true;
    }



    //
    // Highlights

    protected void AddSMExtractHighlight(PDFTextExtract extract)
    {
      AddHighlight(extract,
                   SMExtractColor);
    }

    private void AddImgExtractHighlight(int       pageIndex,
                                        Rectangle boundingBox)
    {
      var pageHighlights = ImageExtractHighlights
        .SafeGet(pageIndex,
                 new List<PDFImageExtract>());

      pageHighlights.Add(new PDFImageExtract
      {
        BoundingBox = boundingBox,
        PageIndex   = pageIndex,
      });

      ImageExtractHighlights[pageIndex] = pageHighlights;
    }

    protected void AddPDFExtractHighlight(PDFTextExtract extract)
    {
      AddHighlight(extract,
                   PDFExtractColor);
    }

    protected void GenerateOutOfExtractHighlights()
    {
      if (PDFElement.IsFullDocument)
        return;

      if (PDFElement.StartIndex > 0)
        HighlightText(PDFElement.StartPage,
                      new HighlightInfo
                      {
                        CharIndex  = 0,
                        CharsCount = PDFElement.StartIndex,
                        Color      = OutOfExtractExtractColor
                      });

      int lastPageCharCount = Document.Pages[PDFElement.EndPage].Text.CountChars;

      if (PDFElement.EndIndex < lastPageCharCount)
        HighlightText(PDFElement.EndPage,
                      new HighlightInfo
                      {
                        CharIndex  = PDFElement.EndIndex,
                        CharsCount = lastPageCharCount - PDFElement.EndIndex,
                        Color      = OutOfExtractExtractColor
                      });

      if (IsPageInClientRect(PDFElement.EndPage) == false)
        Document.Pages[PDFElement.EndPage].Dispose();
    }

    protected void AddIgnoreHighlight(PDFTextExtract extract)
    {
      AddHighlight(extract,
                   IgnoreHighlightColor);
    }

    protected void AddHighlight(PDFTextExtract extract, System.Windows.Media.Color highlightColor)
    {
      for (int pageIdx = extract.StartPage; pageIdx <= extract.EndPage; pageIdx++)
      {
        int pageStartIdx = pageIdx == extract.StartPage ? extract.StartIndex : 0;
        int pageEndIdx   = pageIdx == extract.EndPage ? extract.EndIndex : 0;
        int pageCount = GetTextLength(pageIdx,
                                      pageStartIdx,
                                      pageEndIdx);

        var pageHighlights = ExtractHighlights
          .SafeGet(pageIdx,
                   new List<HighlightInfo>());

        pageHighlights.Add(new HighlightInfo
          {
            CharIndex  = pageStartIdx,
            CharsCount = pageCount,
            Color      = highlightColor
          }
        );

        ExtractHighlights[pageIdx] = pageHighlights;
      }
    }

    #endregion
  }
}
