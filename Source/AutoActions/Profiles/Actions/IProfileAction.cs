using CodectoryCore.Logging;
using System;

namespace AutoActions.Profiles.Actions
{
    public interface IProfileAction
    {
        EventHandler<LogEntry> NewLog { get; set; }

        string ActionDescription { get;}
        string ActionTypeName { get; }
        ActionEndResult RunAction(ApplicationChangedType applicationChangedType);

        bool Enabled { get; set; }

        bool CanSave { get; }

        string CannotSaveMessage { get; }
    }
}
