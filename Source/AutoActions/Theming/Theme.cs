using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoActions.Theming
{
    /// <summary>The theme actually in effect.</summary>
    public enum Theme
    {
        Dark,
        Light
    }

    /// <summary>What the user asked for; System follows Windows' "Choose your default app mode".</summary>
    public enum ThemeSetting
    {
        System,
        Light,
        Dark
    }
}
