#region Copyright (c) 2006-2011 LOKAD SAS. All rights reserved

// You must not remove this notice, or any other, from this software.
// This document is the property of LOKAD SAS and must not be disclosed

#endregion

using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Lokad.CodeDsl
{
    class Program
    {
		public static readonly string[] FileNamePatterns = new[] { "*.ddd" , "*.event", "*.command" };
        static readonly Mutex AppLock = new Mutex(true, "2DB34E68-F80D-4ED0-975A-409C2CDAF241");

        static readonly ConcurrentDictionary<string, string> States = new ConcurrentDictionary<string, string>();
        public static NotifyIcon TrayIcon;
        static string _lastTitle;
        static string _lastMessage;
        static ToolTipIcon _lastIcon;

        static void Main(string[] args)
        {
            if (!AppLock.WaitOne(TimeSpan.Zero, true))
            {
                return;
            }

            var iconStream = Assembly.GetEntryAssembly().GetManifestResourceStream("Lokad.CodeDsl.code_colored.ico");
            TrayIcon = new NotifyIcon
            {
                Visible = true,
            };

            if (iconStream != null)
                TrayIcon.Icon = new Icon(iconStream);

            TrayIcon.Click += TrayIconClick;
            TrayIcon.ContextMenu = new ContextMenu(
                new[] { new MenuItem("Close", (sender, eventArgs) => Close())});

            var path = FigureOutLookupPath(args);
            var info = new DirectoryInfo(path);
	        var tipText = "Looking at " + path;
	        TrayIcon.ShowBalloonTip(5000, "Dsl started", tipText, ToolTipIcon.Info);

			var files = FileNamePatterns.SelectMany(_ => info.GetFiles(_, SearchOption.AllDirectories)).ToArray();

            var notifiers = files
                .Select(f => f.DirectoryName)
                .Distinct()
				.SelectMany(d => FileNamePatterns.Select(_ => new FileSystemWatcher(d, _)))
                .ToArray();

            foreach (var notifier in notifiers)
            {
                notifier.Changed += NotifierOnChanged;
                notifier.EnableRaisingEvents = true;
            }

            var message = string.Format(
                "Lookup path: {0}{1}", 
                info.FullName, Environment.NewLine);

            if (files.Any())
            {
                message += String.Join("\r\n", files.Select(x => x.Name));
            }
            else
            {
                message += "Found no files () to watch";
            }

            message += "\r\n\r\nClick icon to see last message.";


            ShowBalloonTip("Dsl started", message, ToolTipIcon.Info);

            foreach (var fileInfo in files)
            {
                var text = File.ReadAllText(fileInfo.FullName);
                Console.WriteLine("  Watch: {0}", fileInfo.Name);
                Changed(fileInfo.FullName, text);
                try
                {
                    Rebuild(text, fileInfo.FullName);
                }
                catch (Exception ex)
                {
                    ShowBalloonTip("Parse error - " + fileInfo.Name, ex.Message, ToolTipIcon.Error);
                }
            }

            AppDomain.CurrentDomain.ProcessExit += CurrentDomainProcessExit;
            Application.ThreadExit += ApplicationThreadExit;
            Application.Run(new Empty());
        }

        private static void ShowBalloonTip(string title, string message, ToolTipIcon toolTipIcon)
        {
            _lastTitle = title;
            _lastMessage = message;
            _lastIcon = toolTipIcon;

            TrayIcon.ShowBalloonTip(10000, _lastTitle, _lastMessage, _lastIcon);
        }

        static void ApplicationThreadExit(object sender, EventArgs e)
        {
            Close();
        }

        static void CurrentDomainProcessExit(object sender, EventArgs e)
        {
            Close();
        }

        static void TrayIconClick(object sender, EventArgs e)
        {
            ShowBalloonTip(_lastTitle, _lastMessage, _lastIcon);
        }

        static void Close()
        {
            if (TrayIcon != null)
            {
                TrayIcon.Dispose();
            }

            Application.Exit();
        }

        static string FigureOutLookupPath(string[] args)
        {
            var current = Directory.GetCurrentDirectory();

            if (args.Length > 0)
            {
                return args[0];
            }
            var dir = new DirectoryInfo(current);
            switch (dir.Name)
            {
                case "Release":
                case "Debug":
                    return "../../..";
            }
            return dir.FullName;
        }

        static void NotifierOnChanged(object sender, FileSystemEventArgs args)
        {
            if (!File.Exists(args.FullPath)) return;

            try
            {
                var text = File.ReadAllText(args.FullPath);

                if (!Changed(args.FullPath, text))
                    return;


                var message = string.Format("Changed: {1}-{0}", args.Name, args.ChangeType);
                Console.WriteLine(message);
                Rebuild(text, args.FullPath);

                ShowBalloonTip(args.Name, "File rebuilded", ToolTipIcon.Info);
                SystemSounds.Beep.Play();
            }
            catch (IOException) {}
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                ShowBalloonTip("Error - " + args.Name, ex.Message, ToolTipIcon.Error);

                SystemSounds.Exclamation.Play();
            }
        }

        static bool Changed(string path, string value)
        {
            var changed = false;
            States.AddOrUpdate(path, key =>
                {
                    changed = true;
                    return value;
                }, (s, s1) =>
                    {
                        changed = s1 != value;
                        return value;
                    });
            return changed;
        }

        static void Rebuild(string text, string fullPath)
        {
            var dsl = text;
            var generator = new TemplatedGenerator
                {
                    GenerateInterfaceForEntityWithModifiers = "?",
                    TemplateForInterfaceName = "public interface I{0}Aggregate",
                    TemplateForInterfaceMember = "void When({0} c);",
//					ClassNameTemplate = @"[DataContract(Namespace = {1})]
//public partial class {0}",
//					MemberTemplate = "[DataMember(Order = {0})] public {1} {2} {{ get; private set; }}",
					ClassNameTemplate = @"public partial class {0}",
					MemberTemplate = "public {1} {2} {{ get; private set; }}",

                };

  
            File.WriteAllText(Path.ChangeExtension(fullPath, "cs"), GeneratorUtil.Build(dsl, generator));
        }
    }
}