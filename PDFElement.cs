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
// Created On:   2018/10/26 20:56
// Modified On:  2018/11/22 14:07
// Modified By:  Alexis

#endregion




using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Patagames.Pdf.Net;
using Patagames.Pdf.Net.Controls.Wpf;
using SuperMemoAssistant.Extensions;
using SuperMemoAssistant.Interop.SuperMemo.Components.Controls;
using SuperMemoAssistant.Interop.SuperMemo.Components.Types;
using SuperMemoAssistant.Interop.SuperMemo.Elements;
using SuperMemoAssistant.Interop.SuperMemo.Elements.Models;
using SuperMemoAssistant.Interop.SuperMemo.Elements.Types;
using SuperMemoAssistant.Services;

namespace SuperMemoAssistant.Plugins.PDF
{
  public class PDFElement
  {
    #region Properties & Fields - Non-Public

    private PDFCfg Config { get; }

    #endregion




    #region Constructors

    public PDFElement()
    {
      StartPage          = -1;
      EndPage            = -1;
      StartIndex         = -1;
      EndIndex           = -1;
      ReadVerticalOffset = 0;
      SMExtracts         = IPDFExtracts = new List<SelectInfo>();
    }

    #endregion




    #region Properties & Fields - Public

    public int              ElementId          { get; set; }
    public string           FilePath           { get; set; }
    public int              StartPage          { get; set; }
    public int              EndPage            { get; set; }
    public int              StartIndex         { get; set; }
    public int              EndIndex           { get; set; }
    public double           ReadVerticalOffset { get; set; }
    public List<SelectInfo> SMExtracts         { get; set; }

    [JsonIgnore]
    public List<SelectInfo> IPDFExtracts { get; set; }


    [JsonIgnore]
    public bool IsFullDocument => StartPage < 0;

    #endregion




    #region Methods

    public static bool Create(string filePath,
                              int    startPage       = -1,
                              int    endPage         = -1,
                              int    startIdx        = -1,
                              int    endIdx          = -1,
                              int    parentElementId = -1,
                              double verticalOffset  = 0,
                              bool   shouldDisplay   = true)
    {
      PDFElement pdfEl = new PDFElement
      {
        FilePath           = filePath,
        StartPage          = startPage,
        EndPage            = endPage,
        StartIndex         = startIdx,
        EndIndex           = endIdx,
        ReadVerticalOffset = verticalOffset,
      };

      string title;
      string fileName   = Path.GetFileName(filePath);
      string importDate = DateTime.Now.ToString("MMM dd, yyyy, hh:mm:ss");

      try
      {
        using (var pdfDoc = PdfDocument.Load(filePath))
          title = pdfDoc.Title;
      }
      catch (Exception ex)
      {
        return false;
      }

      string elementHtml = string.Format(Const.ElementFormat,
                                         title,
                                         fileName,
                                         pdfEl.GetJsonB64(),
                                         title,
                                         importDate,
                                         filePath);

      IElement parentElement =
        parentElementId > 0
          ? Svc.SMA.Registry.Element[parentElementId]
          : null;

      var elemBuilder =
        new ElementBuilder(ElementType.Topic,
                           elementHtml)
          .WithParent(parentElement);

      if (shouldDisplay == false)
        PDFState.Instance.ReturnToLastElement = true;

      return Svc.SMA.Registry.Element.Add(elemBuilder);
    }

    public static PDFElement TryReadElement(string elText,
                                            int    elementId = -1)
    {
      var reRes = Const.RE_Element.Match(elText);

      if (reRes.Success == false)
        return null;

      try
      {
        string toDeserialize = reRes.Groups[1].Value.Base64Decode();

        var pdfEl = JsonConvert.DeserializeObject<PDFElement>(toDeserialize);

        if (pdfEl != null && elementId > 0)
        {
          pdfEl.ElementId = elementId;

          foreach (IElement childEl in Svc.SMA.Registry.Element[elementId].Children)
            try
            {
              if (!(childEl.ComponentGroup.Components.FirstOrDefault() is IComponentHtml compHtml))
                continue;

              string     childText  = compHtml.Text.Value;
              PDFElement childPdfEl = TryReadElement(childText);

              if (childPdfEl == null)
                continue;

              pdfEl.IPDFExtracts.Add(new SelectInfo
                {
                  StartPage  = childPdfEl.StartPage,
                  EndPage    = childPdfEl.EndPage,
                  StartIndex = childPdfEl.StartIndex,
                  EndIndex   = childPdfEl.EndIndex
                }
              );
            }
            catch (Exception ex) { }
        }

        return pdfEl;
      }
      catch
      {
        return null;
      }
    }

    public SaveResult Save()
    {
      if (ElementId <= 0)
        return SaveToBackup();

      try
      {
        bool saveToControl = Svc.SMA.UI.ElementWindow.CurrentElementId == ElementId;

        if (saveToControl)
        {
          IControlWeb ctrlWeb = Svc.SMA.UI.ElementWindow.ControlGroup.FocusedControl.AsWeb();

          ctrlWeb.Text = UpdateHtml(ctrlWeb.Text);
        }

        else
        {
          var elem       = Svc.SMA.Registry.Element[ElementId];
          var webComp    = elem.ComponentGroup.Components.First().AsWeb();
          var textMember = webComp.Text;

          textMember.Value = UpdateHtml(textMember.Value);
        }

        return SaveResult.OK;
      }
      catch (Exception)
      {
        return SaveToBackup();
      }
    }

    public SaveResult SaveToBackup()
    {
      // TODO: Save to temp file
      return SaveResult.Fail;
    }

    public bool IsPageInBound(int pageNo)
    {
      return IsFullDocument || pageNo >= StartPage && pageNo <= EndPage;
    }

    private string UpdateHtml(string html)
    {
      string newElementDataDiv = string.Format(Const.ElementDataFormat,
                                               GetJsonB64());

      return Const.RE_Element.Replace(html,
                                      newElementDataDiv);
    }

    private string GetJsonB64()
    {
      string elementJson = JsonConvert.SerializeObject(this);

      return elementJson.Base64Encode();
    }

    #endregion




    #region Enums

    public enum SaveResult
    {
      OK             = 0,
      FailWithBackup = 1,
      Fail           = 2,
    }

    #endregion
  }
}