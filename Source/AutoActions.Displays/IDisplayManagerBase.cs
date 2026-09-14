using CodectoryCore;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace AutoActions.Displays
{
    public interface IDisplayManagerBase
    {
        bool GlobalHDRIsActive { get; }
        GraphicsCardType GraphicsCardType { get; }
        DispatchingObservableCollection<Display> Displays { get; set; }
        bool SelectedHDR { get; set; }

        event EventHandler HDRIsActiveChanged;
        event EventHandler<Exception> ExceptionThrown;
        event EventHandler<string> NewLog;


        void ActivateHDR();
        void DeactivateHDR();
        List<Display> GetActiveMonitors();
        ColorDepth GetColorDepth(Display display);
        bool GetHDRState(Display display);
        int GetRefreshRate(Display display);
        Size GetResolution(Display display);
        uint GetUID(uint displayUD);
        void LoadKnownDisplays(List<Display> knownMonitors);
        void SetColorDepth(Display display, ColorDepth colorDepth);
        DISP_CHANGE SetRefreshRate(Display display, int refreshRate);
        DISP_CHANGE SetResolution(Display display, Size resolution);
        DISP_CHANGE SetDisplayMode(Display display, Size? resolution, int? refreshRate);
    }
}