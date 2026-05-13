using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;

namespace AndroidMcp.Adb;

internal static class HierarchyMatcher
{
    private static readonly Regex BoundsRegex = new(@"\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]",
        RegexOptions.Compiled);
    // Default JsonSerializer escapes non-ASCII to \uXXXX (6 bytes per CJK char, ~10 tokens
    // for "完成设备设置"). UnsafeRelaxedJsonEscaping emits the raw UTF-8, ~40% fewer tokens
    // on CJK content. Safe inside our JSON-RPC text payload — we're not embedding in HTML.
    private static readonly JsonSerializerOptions LeanStringOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static UiNode? FindFirst(string xml, Selector sel)
    {
        if (sel.IsEmpty)
        {
            return null;
        }
        XmlDocument doc = new();
        doc.LoadXml(xml);
        if (!string.IsNullOrEmpty(sel.Xpath))
        {
            XPathNavigator nav = doc.CreateNavigator()!;
            XPathNodeIterator it = nav.Select(sel.Xpath);
            if (it.MoveNext() && it.Current is IHasXmlNode hx)
            {
                return ToUiNode(hx.GetNode());
            }
            return null;
        }
        return FindMatching(doc.DocumentElement, sel);
    }

    public static List<UiNode> FindAll(string xml, Selector sel)
    {
        List<UiNode> result = new();
        if (sel.IsEmpty)
        {
            return result;
        }
        XmlDocument doc = new();
        doc.LoadXml(xml);
        if (!string.IsNullOrEmpty(sel.Xpath))
        {
            XPathNavigator nav = doc.CreateNavigator()!;
            XPathNodeIterator it = nav.Select(sel.Xpath);
            while (it.MoveNext())
            {
                if (it.Current is IHasXmlNode hx)
                {
                    result.Add(ToUiNode(hx.GetNode()));
                }
            }
            return result;
        }
        CollectMatching(doc.DocumentElement, sel, result);
        return result;
    }

    private static UiNode? FindMatching(XmlNode? el, Selector sel)
    {
        if (el is null)
        {
            return null;
        }
        if (el.Attributes is not null && Matches(el, sel))
        {
            return ToUiNode(el);
        }
        foreach (XmlNode child in el.ChildNodes)
        {
            UiNode? hit = FindMatching(child, sel);
            if (hit is not null)
            {
                return hit;
            }
        }
        return null;
    }

    private static void CollectMatching(XmlNode? el, Selector sel, List<UiNode> sink)
    {
        if (el is null)
        {
            return;
        }
        if (el.Attributes is not null && Matches(el, sel))
        {
            sink.Add(ToUiNode(el));
        }
        foreach (XmlNode child in el.ChildNodes)
        {
            CollectMatching(child, sel, sink);
        }
    }

    private static bool Matches(XmlNode el, Selector sel)
    {
        string text = el.Attributes?["text"]?.Value ?? string.Empty;
        string rid = el.Attributes?["resource-id"]?.Value ?? string.Empty;
        string desc = el.Attributes?["content-desc"]?.Value ?? string.Empty;
        string cls = el.Attributes?["class"]?.Value ?? string.Empty;
        string pkg = el.Attributes?["package"]?.Value ?? string.Empty;

        if (!string.IsNullOrEmpty(sel.Text))
        {
            bool ok = sel.Partial ? text.Contains(sel.Text) : text == sel.Text;
            if (!ok)
            { return false; }
        }
        if (!string.IsNullOrEmpty(sel.ResourceId) && rid != sel.ResourceId)
        { return false; }
        if (!string.IsNullOrEmpty(sel.ContentDesc))
        {
            bool ok = sel.Partial ? desc.Contains(sel.ContentDesc) : desc == sel.ContentDesc;
            if (!ok)
            { return false; }
        }
        if (!string.IsNullOrEmpty(sel.ClassName) && cls != sel.ClassName)
        { return false; }
        if (!string.IsNullOrEmpty(sel.Package) && pkg != sel.Package)
        { return false; }
        if (!MatchBool(el, "checkable", sel.Checkable))
        { return false; }
        if (!MatchBool(el, "checked", sel.Checked))
        { return false; }
        if (!MatchBool(el, "clickable", sel.Clickable))
        { return false; }
        if (!MatchBool(el, "enabled", sel.Enabled))
        { return false; }
        if (!MatchBool(el, "focusable", sel.Focusable))
        { return false; }
        if (!MatchBool(el, "focused", sel.Focused))
        { return false; }
        if (!MatchBool(el, "long-clickable", sel.LongClickable))
        { return false; }
        if (!MatchBool(el, "password", sel.Password))
        { return false; }
        if (!MatchBool(el, "scrollable", sel.Scrollable))
        { return false; }
        if (!MatchBool(el, "selected", sel.Selected))
        { return false; }
        return true;
    }

    private static bool MatchBool(XmlNode el, string attr, bool? expected)
    {
        if (expected is null)
        { return true; }
        string raw = el.Attributes?[attr]?.Value ?? string.Empty;
        bool actual = string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        return actual == expected.Value;
    }

    private static UiNode ToUiNode(XmlNode el)
    {
        UiNode n = new()
        {
            Text = el.Attributes?["text"]?.Value ?? string.Empty,
            ResourceId = el.Attributes?["resource-id"]?.Value ?? string.Empty,
            ContentDesc = el.Attributes?["content-desc"]?.Value ?? string.Empty,
            ClassName = el.Attributes?["class"]?.Value ?? string.Empty
        };
        string bounds = el.Attributes?["bounds"]?.Value ?? string.Empty;
        Match m = BoundsRegex.Match(bounds);
        if (m.Success)
        {
            n.Left = int.Parse(m.Groups[1].Value);
            n.Top = int.Parse(m.Groups[2].Value);
            n.Right = int.Parse(m.Groups[3].Value);
            n.Bottom = int.Parse(m.Groups[4].Value);
        }
        return n;
    }

    private static string JsonStr(string s) => JsonSerializer.Serialize(s, LeanStringOpts);

    public static string Fingerprint(string xml)
    {
        long sum = 0;
        for (int i = 0; i < xml.Length; i++)
        { sum = sum * 131 + xml[i]; }
        return $"{xml.Length}:{sum:x}";
    }

    // Compact JSON representation: 5-12x smaller than the agent's XML.
    // Keys: t=class shortName, b="l,t,r,b", x=text, d=contentDesc, i=resource-id (stripped),
    //       c=clickable, s=scrollable, f=focused, cb=checkable, ch=checked, p=password, e=0 if disabled.
    // Default behavior drops invisible (zero-area) nodes; interactiveOnly also drops pure layout
    // containers (no text/desc/id/clickable/scrollable/focused).
    public static string ToLean(string xml, bool interactiveOnly)
    {
        XmlDocument doc = new();
        doc.LoadXml(xml);
        StringBuilder sb = new(8192);
        sb.Append("{\"format\":\"lean\",\"nodes\":[");
        bool first = true;
        EmitLean(doc.DocumentElement, sb, interactiveOnly, ref first);
        sb.Append("]}");
        return sb.ToString();
    }

    private static void EmitLean(XmlNode? el, StringBuilder sb, bool interactiveOnly, ref bool first)
    {
        if (el is null)
        {
            return;
        }
        if (el.Name == "node" && el.Attributes is not null && ShouldEmit(el, interactiveOnly, out string body))
        {
            if (!first)
            {
                sb.Append(',');
            }
            first = false;
            sb.Append(body);
        }
        foreach (XmlNode child in el.ChildNodes)
        {
            EmitLean(child, sb, interactiveOnly, ref first);
        }
    }

    private static bool ShouldEmit(XmlNode el, bool interactiveOnly, out string body)
    {
        body = string.Empty;
        string bounds = el.Attributes?["bounds"]?.Value ?? string.Empty;
        Match m = BoundsRegex.Match(bounds);
        if (!m.Success)
        {
            return false;
        }
        int left = int.Parse(m.Groups[1].Value);
        int top = int.Parse(m.Groups[2].Value);
        int right = int.Parse(m.Groups[3].Value);
        int bottom = int.Parse(m.Groups[4].Value);
        if (right <= left || bottom <= top)
        {
            return false;
        }

        string text = el.Attributes?["text"]?.Value ?? string.Empty;
        string desc = el.Attributes?["content-desc"]?.Value ?? string.Empty;
        string rid = el.Attributes?["resource-id"]?.Value ?? string.Empty;
        bool clickable = string.Equals(el.Attributes?["clickable"]?.Value, "true", StringComparison.Ordinal);
        bool scrollable = string.Equals(el.Attributes?["scrollable"]?.Value, "true", StringComparison.Ordinal);
        bool focused = string.Equals(el.Attributes?["focused"]?.Value, "true", StringComparison.Ordinal);
        bool checkable = string.Equals(el.Attributes?["checkable"]?.Value, "true", StringComparison.Ordinal);
        bool isChecked = string.Equals(el.Attributes?["checked"]?.Value, "true", StringComparison.Ordinal);
        bool enabled = !string.Equals(el.Attributes?["enabled"]?.Value, "false", StringComparison.Ordinal);
        bool password = string.Equals(el.Attributes?["password"]?.Value, "true", StringComparison.Ordinal);

        if (interactiveOnly)
        {
            bool relevant = !string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(desc)
                || !string.IsNullOrEmpty(rid) || clickable || scrollable || focused;
            if (!relevant)
            {
                return false;
            }
        }

        string cls = el.Attributes?["class"]?.Value ?? string.Empty;
        string shortCls = cls;
        int lastDot = cls.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < cls.Length - 1)
        {
            shortCls = cls.Substring(lastDot + 1);
        }

        StringBuilder b = new(96);
        b.Append("{\"t\":").Append(JsonStr(shortCls));
        b.Append(",\"b\":\"").Append(left).Append(',').Append(top).Append(',').Append(right).Append(',').Append(bottom).Append('"');
        if (!string.IsNullOrEmpty(text))
        {
            b.Append(",\"x\":").Append(JsonStr(text));
        }
        if (!string.IsNullOrEmpty(desc))
        {
            b.Append(",\"d\":").Append(JsonStr(desc));
        }
        if (!string.IsNullOrEmpty(rid))
        {
            int slash = rid.IndexOf(":id/", StringComparison.Ordinal);
            string shortId = slash >= 0 ? rid.Substring(slash + 4) : rid;
            b.Append(",\"i\":").Append(JsonStr(shortId));
        }
        if (clickable)
        { b.Append(",\"c\":1"); }
        if (scrollable)
        { b.Append(",\"s\":1"); }
        if (focused)
        { b.Append(",\"f\":1"); }
        if (checkable)
        { b.Append(",\"cb\":1"); }
        if (isChecked)
        { b.Append(",\"ch\":1"); }
        if (!enabled)
        { b.Append(",\"e\":0"); }
        if (password)
        { b.Append(",\"p\":1"); }
        b.Append('}');
        body = b.ToString();
        return true;
    }
}
