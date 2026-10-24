using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace QuickReplyLite
{
    public static class PreviewText
    {
        // Count text elements instead of UTF-16 code units, so emoji and accents stay intact.
        public static string Wrap(string text, int columns = 25, int maxLines = 16)
        {
            if (columns < 1 || maxLines < 1) throw new ArgumentOutOfRangeException();
            string value = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ");
            StringBuilder output = new StringBuilder(); int column = 0, line = 1, lastLength = 0;
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(value);
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                if (element == "\n" || column == columns)
                {
                    if (line == maxLines) { if (column == columns && lastLength > 0) output.Remove(output.Length - lastLength, lastLength); output.Append("…"); return output.ToString(); }
                    output.Append("\r\n"); line++; column = 0;
                    if (element == "\n") continue;
                }
                output.Append(element); column++; lastLength = element.Length;
            }
            return output.ToString();
        }

        public static string ForPhrase(Phrase phrase)
        {
            string body = PhraseContent.PlainText(phrase);
            if (phrase.Attachments.Count > 0) body += "\r\n附件：" + String.Join("、", phrase.Attachments.Select(a => a.Name));
            return Wrap(body.Trim(), 25, 12) + "\r\n\r\n双击仅粘贴；左侧箭头发送\r\n长按拖动；右键或F2编辑";
        }
    }

    public enum DeliveryMode { PasteOnly, Send }
    public sealed class DeliveryStep
    {
        public string Text { get; internal set; }
        public int[] AttachmentIndices { get; internal set; }
        public bool PressEnter { get; internal set; }
    }
    public static class DeliveryPlan
    {
        public static List<DeliveryStep> Build(Phrase phrase, DeliveryMode mode)
        {
            bool submit = mode == DeliveryMode.Send; List<DeliveryStep> result = new List<DeliveryStep>();
            if (phrase.InlineAttachments && submit)
                foreach (SendPart part in PhraseContent.Build(phrase)) result.Add(new DeliveryStep { Text = part.Text, AttachmentIndices = part.AttachmentIndex >= 0 ? new[] { part.AttachmentIndex } : new int[0], PressEnter = true });
            else
            {
                string text = PhraseContent.PlainText(phrase);
                if (!String.IsNullOrWhiteSpace(text)) result.Add(new DeliveryStep { Text = text, AttachmentIndices = new int[0], PressEnter = submit });
                if (phrase.Attachments.Count > 0) result.Add(new DeliveryStep { AttachmentIndices = Enumerable.Range(0, phrase.Attachments.Count).ToArray(), PressEnter = submit });
            }
            return result;
        }
    }
}
