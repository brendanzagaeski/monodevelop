// 
// NavigateToDialog.cs
//  
// Author:
//       Mike Krüger <mkrueger@novell.com>
// 
// Copyright (c) 2010 Novell, Inc (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Text;
using Gdk;
using Gtk;
using MonoDevelop.Core;
using MonoDevelop.Core.Text;
using MonoDevelop.Projects;
using ICSharpCode.NRefactory.TypeSystem;
using MonoDevelop.Ide.TypeSystem;

namespace MonoDevelop.Ide.NavigateToDialog
{
	public enum SearchResultType
	{
		File,
		Type,
		Member
	}

	abstract class SearchResult
	{
		protected string match;
		
		public virtual string GetMarkupText (Widget widget)
		{
			return HighlightMatch (widget, PlainText, match);
		}

		public virtual string GetDescriptionMarkupText (Widget widget)
		{
			return GLib.Markup.EscapeText (Description);
		}


		public abstract SearchResultType SearchResultType { get; }
		public abstract string PlainText  { get; }

		public int Rank { get; private set; }

		public virtual int Row { get { return -1; } }
		public virtual int Column { get { return -1; } }
		
		public abstract string File { get; }
		public abstract Gdk.Pixbuf Icon { get; }
		
		public abstract string Description { get; }
		public string MatchedString { get; private set;}
		
		public SearchResult (string match, string matchedString, int rank)
		{
			this.match = match;
			this.MatchedString = matchedString;
			Rank = rank;
		}
		
		protected static string HighlightMatch (Widget widget, string text, string toMatch)
		{
			var lane = StringMatcher.GetMatcher (toMatch, true).GetMatch (text);
			StringBuilder result = new StringBuilder ();
			if (lane != null) {
				int lastPos = 0;
				for (int n=0; n < lane.Length; n++) {
					int pos = lane[n];
					if (pos - lastPos > 0)
						MarkupUtilities.AppendEscapedString (result, text.Substring (lastPos, pos - lastPos));
					result.Append ("<span foreground=\"#4d4d4d\" font_weight=\"bold\">");
					MarkupUtilities.AppendEscapedString (result, text[pos].ToString ());
					result.Append ("</span>");
					lastPos = pos + 1;
				}
				if (lastPos < text.Length)
					MarkupUtilities.AppendEscapedString (result, text.Substring (lastPos, text.Length - lastPos));
			} else {
				MarkupUtilities.AppendEscapedString (result, text);
			}
			return result.ToString ();
		}
	}
	
	class TypeSearchResult : MemberSearchResult
	{
		ITypeDefinition type;
			
		public override SearchResultType SearchResultType { get { return SearchResultType.Type; } }

		public override string File {
			get { return type.Region.FileName; }
		}
		
		public override Gdk.Pixbuf Icon {
			get {
				return ImageService.GetPixbuf (type.GetStockIcon (), IconSize.Menu);
			}
		}
		
		public override int Row {
			get { return type.Region.BeginLine; }
		}
		
		public override int Column {
			get { return type.Region.BeginColumn; }
		}
		
		public override string PlainText {
			get {
				return Ambience.GetString (type, Flags);
			}
		}

		public override string Description {
			get {
				string loc;
				if (type.GetSourceProject () != null) {
					loc = GettextCatalog.GetString ("project {0}", type.GetSourceProject ().Name);
				} else {
					loc = GettextCatalog.GetString ("file {0}", type.Region.FileName);
				}

				switch (type.Kind) {
				case TypeKind.Interface:
					return GettextCatalog.GetString ("interface ({0})", loc);
				case TypeKind.Struct:
					return GettextCatalog.GetString ("struct ({0})", loc);
				case TypeKind.Delegate:
					return GettextCatalog.GetString ("delegate ({0})", loc);
				case TypeKind.Enum:
					return GettextCatalog.GetString ("enumeration ({0})", loc);
				default:
					return GettextCatalog.GetString ("class ({0})", loc);
				}
			}
		}
		
		public override string GetMarkupText (Widget widget)
		{
			if (useFullName)
				return HighlightMatch (widget, Ambience.GetString (type, Flags), match);
			return HighlightMatch (widget, type.Name, match);
		}
		
		public TypeSearchResult (string match, string matchedString, int rank, ITypeDefinition type, bool useFullName) : base (match, matchedString, rank, null, useFullName)
		{
			this.type = type;
		}
	}
	
	class FileSearchResult: SearchResult
	{
		ProjectFile file;
		bool useFileName;

		public override SearchResultType SearchResultType { get { return SearchResultType.File; } }

		public override string PlainText {
			get {
				if (useFileName)
					return System.IO.Path.GetFileName (file.FilePath);
				return GetRelProjectPath (file);
			}
		}
		 
		public override string File {
			get {
				return file.FilePath;
			}
		}
		
		public override Gdk.Pixbuf Icon {
			get {
				return DesktopService.GetPixbufForFile (file.FilePath, IconSize.Menu);
			}
		}

		public override string Description {
			get {
				if (useFileName)
					return file.Project != null
						? GettextCatalog.GetString ("file \"{0}\" in project \"{1}\"", GetRelProjectPath (file), file.Project.Name)
						: GettextCatalog.GetString ("file \"{0}\"", GetRelProjectPath (file));
				return file.Project != null ? GettextCatalog.GetString ("file in project \"{0}\"", file.Project.Name) : "";
			}
		}
		
		public FileSearchResult (string match, string matchedString, int rank, ProjectFile file, bool useFileName)
							: base (match, matchedString, rank)
		{
			this.file = file;
			this.useFileName = useFileName;
		}
		
		internal static string GetRelProjectPath (ProjectFile file)
		{
			if (file.Project != null)
				return file.ProjectVirtualPath;
			return file.FilePath;
		}
	}
	
	class MemberSearchResult : SearchResult
	{
		protected bool useFullName;
		protected IMember member;
		
		public override SearchResultType SearchResultType { get { return SearchResultType.Member; } }

		protected virtual OutputFlags Flags {
			get {
				return OutputFlags.IncludeParameters | OutputFlags.IncludeGenerics
					| (useFullName  ? OutputFlags.UseFullName : OutputFlags.None);
			}
		}
		
		public override string PlainText {
			get {
				return Ambience.GetString (member, Flags);
			}
		}
		
		public override string File {
			get { return member.DeclaringTypeDefinition.Region.FileName; }
		}
		
		public override Gdk.Pixbuf Icon {
			get {
				return ImageService.GetPixbuf (member.GetStockIcon (), IconSize.Menu);
			}
		}
		
		public override int Row {
			get { return member.Region.BeginLine; }
		}
		
		public override int Column {
			get { return member.Region.BeginColumn; }
		}
		
		public override string Description {
			get {
				string loc = GettextCatalog.GetString ("type \"{0}\"", member.DeclaringType.Name);

				switch (member.EntityType) {
				case EntityType.Field:
					return GettextCatalog.GetString ("field ({0})", loc);
				case EntityType.Property:
					return GettextCatalog.GetString ("property ({0})", loc);
				case EntityType.Indexer:
					return GettextCatalog.GetString ("indexer ({0})", loc);
				case EntityType.Event:
					return GettextCatalog.GetString ("event ({0})", loc);
				case EntityType.Method:
					return GettextCatalog.GetString ("method ({0})", loc);
				case EntityType.Operator:
					return GettextCatalog.GetString ("operator ({0})", loc);
				case EntityType.Constructor:
					return GettextCatalog.GetString ("constructor ({0})", loc);
				case EntityType.Destructor:
					return GettextCatalog.GetString ("destrutcor ({0})", loc);
				default:
					throw new NotSupportedException (member.EntityType + " is not supported.");
				}
			}
		}
		
		public MemberSearchResult (string match, string matchedString, int rank, IMember member, bool useFullName) : base (match, matchedString, rank)
		{
			this.member = member;
			this.useFullName = useFullName;
		}
		
		public override string GetMarkupText (Widget widget)
		{
			if (useFullName)
				return HighlightMatch (widget, Ambience.GetString (member, Flags), match);
			OutputSettings settings = new OutputSettings (Flags | OutputFlags.IncludeMarkup);
			settings.EmitNameCallback = delegate (object domVisitable, string outString) {
				if (member == domVisitable)
					outString = HighlightMatch (widget, outString, match);
				return outString;
			};
			return Ambience.GetString (member, settings);
		}
		
		internal Ambience Ambience { 
			get;
			set;
		}
	}
}
