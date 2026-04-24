using System.Web.Script.Serialization;

namespace MicroseismicSync.Utilities
{
    public static class JsonUtility
    {
        public static T Deserialize<T>(string json)
        {
            return CreateSerializer().Deserialize<T>(json);
        }

        public static string Serialize(object value)
        {
            return CreateSerializer().Serialize(value);
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 128,
            };
        }
    }
}
