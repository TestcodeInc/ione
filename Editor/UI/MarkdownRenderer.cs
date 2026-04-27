using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Ione.UI
{
    // Lightweight IMGUI markdown renderer. Supports headings (# ## ###),
    // **bold**, *italic*, `inline code`, fenced ```code``` blocks,
    // bullet (- / *) and numbered (1.) lists, [link](url), and --- rules.
    // Body text loses selectability (rich text requires plain Label), but
    // fenced code blocks stay selectable so users can copy snippets.
    [InitializeOnLoad]
    internal static class MarkdownRenderer
    {
        static GUIStyle body, h1, h2, h3, listItem, codeBlock, hr;
        static bool stylesReady;

        // Wire link clicks once at editor load. The <a href="..."> tags we
        // emit during InlineFormat surface here as a HyperLinkClicked event;
        // the href arrives in args.hyperLinkData.
        static MarkdownRenderer()
        {
            EditorGUI.hyperLinkClicked += OnHyperLinkClicked;
        }

        static void OnHyperLinkClicked(EditorWindow window, HyperLinkClickedEventArgs args)
        {
            if (args.hyperLinkData != null && args.hyperLinkData.TryGetValue("href", out var url))
            {
                if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
            }
        }

        static void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;

            body = new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true };
            h1 = new GUIStyle(body) { fontSize = body.fontSize + 6, fontStyle = FontStyle.Bold, margin = new RectOffset(0, 0, 6, 2) };
            h2 = new GUIStyle(body) { fontSize = body.fontSize + 3, fontStyle = FontStyle.Bold, margin = new RectOffset(0, 0, 4, 2) };
            h3 = new GUIStyle(body) { fontSize = body.fontSize + 1, fontStyle = FontStyle.Bold, margin = new RectOffset(0, 0, 3, 2) };
            listItem = new GUIStyle(body) { padding = new RectOffset(16, 0, 0, 0) };
            codeBlock = new GUIStyle(EditorStyles.textArea) { wordWrap = false, font = EditorStyles.standardFont };
            hr = new GUIStyle { fixedHeight = 1, margin = new RectOffset(0, 0, 6, 6) };
        }

        public static void Draw(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return;
            EnsureStyles();
            foreach (var block in Parse(markdown))
                DrawBlock(block);
        }

        // ---- block model ----

        enum BlockKind { Paragraph, H1, H2, H3, Bullet, Numbered, Code, Hr }

        struct Block
        {
            public BlockKind Kind;
            public string Text;        // paragraph / heading / list item / hr (unused) / code body
            public string Marker;      // "1.", "2." for numbered lists; null otherwise
        }

        static IEnumerable<Block> Parse(string md)
        {
            var lines = md.Replace("\r\n", "\n").Split('\n');
            var paraBuf = new StringBuilder();
            bool inCode = false;
            var codeBuf = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];

                if (inCode)
                {
                    if (raw.TrimStart().StartsWith("```"))
                    {
                        yield return new Block { Kind = BlockKind.Code, Text = codeBuf.ToString().TrimEnd('\n') };
                        codeBuf.Length = 0;
                        inCode = false;
                    }
                    else
                    {
                        if (codeBuf.Length > 0) codeBuf.Append('\n');
                        codeBuf.Append(raw);
                    }
                    continue;
                }

                var trimmed = raw.TrimStart();
                if (trimmed.StartsWith("```"))
                {
                    if (paraBuf.Length > 0) { yield return Para(paraBuf); paraBuf.Length = 0; }
                    inCode = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(raw))
                {
                    if (paraBuf.Length > 0) { yield return Para(paraBuf); paraBuf.Length = 0; }
                    continue;
                }

                if (trimmed == "---" || trimmed == "***" || trimmed == "___")
                {
                    if (paraBuf.Length > 0) { yield return Para(paraBuf); paraBuf.Length = 0; }
                    yield return new Block { Kind = BlockKind.Hr };
                    continue;
                }

                if (trimmed.StartsWith("# "))
                {
                    if (paraBuf.Length > 0) { yield return Para(paraBuf); paraBuf.Length = 0; }
                    yield return new Block { Kind = BlockKind.H1, Text = InlineFormat(trimmed.Substring(2)) };
                    continue;
                }
                if (trimmed.StartsWith("## "))
                {
                    if (paraBuf.Length > 0) { yield return Para(paraBuf); paraBuf.Length = 0; }
                    yield return new Block { Kind = BlockKind.H2, Text = InlineFormat(trimmed.Substring(3)) };
                    continue;
                }
                if (trimmed.StartsWith("### "))
                {
                    if (paraBuf.Length > 0) { yield return Para(paraBuf); paraBuf.Length = 0; }
                    yield return new Block { Kind = BlockKind.H3, Text = InlineFormat(trimmed.Substring(4)) };
                    continue;
                }

                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    if (paraBuf.Length > 0) { yield return Para(paraBuf); paraBuf.Length = 0; }
                    yield return new Block { Kind = BlockKind.Bullet, Text = InlineFormat(trimmed.Substring(2)) };
                    continue;
                }

                var num = NumberedPrefix(trimmed);
                if (num != null)
                {
                    if (paraBuf.Length > 0) { yield return Para(paraBuf); paraBuf.Length = 0; }
                    yield return new Block { Kind = BlockKind.Numbered, Text = InlineFormat(trimmed.Substring(num.Length + 1)), Marker = num };
                    continue;
                }

                if (paraBuf.Length > 0) paraBuf.Append(' ');
                paraBuf.Append(trimmed);
            }

            if (inCode && codeBuf.Length > 0)
                yield return new Block { Kind = BlockKind.Code, Text = codeBuf.ToString().TrimEnd('\n') };
            if (paraBuf.Length > 0)
                yield return Para(paraBuf);
        }

        static Block Para(StringBuilder sb) =>
            new Block { Kind = BlockKind.Paragraph, Text = InlineFormat(sb.ToString()) };

        // Returns "1", "2", ... if the line starts with "<digits>. ", else null.
        static string NumberedPrefix(string s)
        {
            int i = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            if (i == 0) return null;
            if (i + 1 >= s.Length || s[i] != '.' || s[i + 1] != ' ') return null;
            return s.Substring(0, i);
        }

        // ---- inline formatting (bold, italic, inline code, links) ----

        static readonly Regex BoldRx       = new Regex(@"\*\*(.+?)\*\*",  RegexOptions.Compiled);
        static readonly Regex ItalicRx     = new Regex(@"(?<!\*)\*(?!\*)([^\*\n]+?)\*(?!\*)", RegexOptions.Compiled);
        static readonly Regex InlineCodeRx = new Regex(@"`([^`\n]+?)`",   RegexOptions.Compiled);
        static readonly Regex LinkRx       = new Regex(@"\[([^\]]+)\]\(([^)\s]+)\)", RegexOptions.Compiled);

        static string InlineFormat(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // Order matters: code first so emphasis inside code isn't transformed.
            var protectedSpans = new List<string>();
            s = InlineCodeRx.Replace(s, m =>
            {
                var token = "" + protectedSpans.Count + "";
                protectedSpans.Add("<color=#d6c98a>" + Escape(m.Groups[1].Value) + "</color>");
                return token;
            });
            s = LinkRx.Replace(s, m =>
            {
                var token = "" + protectedSpans.Count + "";
                var href = m.Groups[2].Value.Replace("\"", "&quot;");
                protectedSpans.Add("<a href=\"" + href + "\"><color=#5cb6ff><u>" + Escape(m.Groups[1].Value) + "</u></color></a>");
                return token;
            });
            s = BoldRx.Replace(s, "<b>$1</b>");
            s = ItalicRx.Replace(s, "<i>$1</i>");
            for (int i = 0; i < protectedSpans.Count; i++)
                s = s.Replace("" + i + "", protectedSpans[i]);
            return s;
        }

        // Rich-text tags use angle brackets, so escape them inside code/links
        // to keep them rendering literally.
        static string Escape(string s) => s.Replace("<", "&lt;").Replace(">", "&gt;");

        // ---- drawing ----

        static void DrawBlock(Block b)
        {
            switch (b.Kind)
            {
                case BlockKind.Paragraph: DrawWrapped(b.Text, body); break;
                case BlockKind.H1:        DrawWrapped(b.Text, h1);   break;
                case BlockKind.H2:        DrawWrapped(b.Text, h2);   break;
                case BlockKind.H3:        DrawWrapped(b.Text, h3);   break;
                case BlockKind.Bullet:    DrawWrapped("•  " + b.Text, listItem); break;
                case BlockKind.Numbered:  DrawWrapped(b.Marker + ".  " + b.Text, listItem); break;
                case BlockKind.Code:      DrawCodeBlock(b.Text); break;
                case BlockKind.Hr:        DrawHr(); break;
            }
        }

        static void DrawWrapped(string richText, GUIStyle style)
        {
            var content = new GUIContent(richText);
            var width = Mathf.Max(80, EditorGUIUtility.currentViewWidth - 80);
            var height = style.CalcHeight(content, width);
            var rect = GUILayoutUtility.GetRect(width, height, style);
            GUI.Label(rect, content, style);
        }

        static void DrawCodeBlock(string code)
        {
            var content = new GUIContent(code);
            var width = Mathf.Max(80, EditorGUIUtility.currentViewWidth - 80);
            var height = codeBlock.CalcHeight(content, width) + 4;
            EditorGUILayout.SelectableLabel(code, codeBlock, GUILayout.Height(height));
        }

        static void DrawHr()
        {
            var rect = GUILayoutUtility.GetRect(1, 1, hr);
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.15f));
        }
    }
}
