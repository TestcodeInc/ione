using System.Collections.Generic;

namespace Ione.Tools
{
    // Tool return type for anything that attaches binary data (images).
    // Plain text tools still return string; the router wraps them.
    public class ToolOutput
    {
        public string Content;
        public bool IsError;
        public List<ImageData> Images;

        public static ToolOutput FromText(string content, bool isError)
            => new ToolOutput { Content = content, IsError = isError };
    }

    public class ImageData
    {
        public string MediaType; // e.g. "image/png"
        public string Base64;    // raw base64, no data: prefix
    }
}
