using Newtonsoft.Json;

namespace AutoActions.Profiles
{
    /// <summary>
    /// Copies a profile or an action by writing it out and reading it back with the same settings the
    /// settings file uses. Actions are polymorphic and each type has its own fields, so a copy that
    /// does not go through the serialiser would have to be written again for every action type - and
    /// forgotten for the next one. What survives a save is exactly what a copy has to carry.
    /// </summary>
    public static class DeepCopy
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Objects,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple
        };

        public static T Of<T>(T source) where T : class
        {
            if (source == null)
                return null;
            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(source, Settings), Settings);
        }
    }
}
