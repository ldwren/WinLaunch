﻿using SocketIOClient;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace WinLaunch
{
    public class AvalonExtension : DependencyObject
    {
        public static string GetBindableText(DependencyObject obj)
        {
            return (string)obj.GetValue(BindableTextProperty);
        }

        public static void SetBindableText(DependencyObject obj, string value)
        {
            obj.SetValue(BindableTextProperty, value);
        }

        public static readonly DependencyProperty BindableTextProperty =
            DependencyProperty.RegisterAttached("BindableText", typeof(string), typeof(AvalonExtension), new FrameworkPropertyMetadata
            {
                BindsTwoWayByDefault = false,
                PropertyChangedCallback = (obj, e) =>
                {
                    var editor = (ICSharpCode.AvalonEdit.TextEditor)obj;

                    string text = GetBindableText(editor);

                    if (editor.Text != text)
                    {
                        editor.Text = text;
                    }
                }
            });
    }

    public class AssistantExecutedCommand : DependencyObject
    {
        public string Text { get; set; }
        public string File { get; set; }
        public string Parameters { get; set; }

        public bool showParameters { get; set; } = false;

        public string Output
        {
            get { return (string)GetValue(OutputProperty); }
            set { SetValue(OutputProperty, value); }
        }

        public static readonly DependencyProperty OutputProperty =
            DependencyProperty.Register("Output", typeof(string), typeof(AssistantExecutedCommand), new PropertyMetadata(""));


        public Visibility OutputVisible
        {
            get { return (Visibility)GetValue(OutputVisibleProperty); }
            set { SetValue(OutputVisibleProperty, value); }
        }

        public static readonly DependencyProperty OutputVisibleProperty =
            DependencyProperty.Register("OutputVisible", typeof(Visibility), typeof(AssistantExecutedCommand), new PropertyMetadata(Visibility.Collapsed));

        public void SetOuput(string output)
        {
            Output = output;
            OutputVisible = Visibility.Visible;
        }

        public AssistantExecutedCommand(string file, string parameters)
        {
            File = file;

            if (parameters != null)
            {
                showParameters = true;
                Parameters = parameters;
            }
        }
    }


    public class AssistantExecutedPython : DependencyObject
    {
        public string Code { get; set; }

        public string Output
        {
            get { return (string)GetValue(OutputProperty); }
            set { SetValue(OutputProperty, value); }
        }

        public static readonly DependencyProperty OutputProperty =
            DependencyProperty.Register("Output", typeof(string), typeof(AssistantExecutedCommand), new PropertyMetadata(""));


        public Visibility OutputVisible
        {
            get { return (Visibility)GetValue(OutputVisibleProperty); }
            set { SetValue(OutputVisibleProperty, value); }
        }

        public static readonly DependencyProperty OutputVisibleProperty =
            DependencyProperty.Register("OutputVisible", typeof(Visibility), typeof(AssistantExecutedCommand), new PropertyMetadata(Visibility.Collapsed));

        public void SetOuput(string output)
        {
            Output = output;
            OutputVisible = Visibility.Visible;
        }
    }

    partial class MainWindow : Window
    {
        //TODO: run threaded to not block the UI thread
        private async Task<string> ExecuteProcessAndGetOutput(string file, string parameters)
        {
            return await Task<string>.Run(() =>
            {
                string output = string.Empty;
                parameters += " && exit";
                using (Process process = new Process())
                {
                    process.StartInfo.FileName = file;
                    process.StartInfo.Arguments = parameters;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;

                    // Do not create the black window
                    process.StartInfo.CreateNoWindow = true;

                    // Hides the window
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

                    process.Start();

                    process.BeginOutputReadLine();
                    process.OutputDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            // Handle the output line here.
                            output += args.Data + "\n";
                        }
                    };

                    // Wait for the process to exit with a 10s timeout.
                    if (!process.WaitForExit(10000))
                    {
                        process.Kill();
                    }

                    //ensure process has exited even after being killed
                    process.WaitForExit();
                }

                return output;
            });
        }

        void shell_execute(SocketIOResponse args)
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    string file = args.GetValue<string>();
                    string parameters = args.GetValue<string>(1);

                    var commandUI = new AssistantExecutedCommand(file, parameters)
                    {
                        Text = TranslationSource.Instance["AssistantExecutedCommand"]
                    };

                    icAssistantContent.Items.Add(commandUI);

                    AdjustAssistantMessageSpacing();
                    scvAssistant.ScrollToBottom();

                    if (!Settings.CurrentSettings.ExecuteAssistantCommands)
                    {
                        try
                        {
                            await args.CallbackAsync("User disabled commands, they can be enabled again in the settings");
                        }
                        catch { }

                        return;
                    }

                    try
                    {
                        string output = await ExecuteProcessAndGetOutput(file, parameters);
                        await args.CallbackAsync(output);

                        if (!string.IsNullOrEmpty(output))
                        {
                            //update output in the UI
                            commandUI.SetOuput(output.TrimEnd(new char[] { '\n', '\r' }));
                        }
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            await args.CallbackAsync(ex.Message);
                        }
                        catch { }
                    }
                }
                catch { }
            }));
        }

        private string RunPythonAndGetOutput(string code)
        {
            
            
            
            
            string output = string.Empty;
            string error = string.Empty;

            //write text into a .py file
            string tempFile = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tempFile, code);

            //print the code to trace
            Console.WriteLine(tempFile);

            //run python
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "python";
                process.StartInfo.Arguments = tempFile;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;

                process.StartInfo.CreateNoWindow = true; // Do not create the black window
                process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden; // Hides the window

                process.Start();

                output = process.StandardOutput.ReadToEnd();
                try
                {
                    error = process.StandardError.ReadToEnd();
                }
                catch
                {
                    Trace.WriteLine("No error in python execution");
                }

                if (!process.WaitForExit(5)) // Wait for the process to exit with a timeout.
                {
                    process.Kill(); // Forcefully kill the process if it doesn't exit in time.
                }
            }

            return output + "\n" + error;
        }

        void run_python(SocketIOResponse args)
        {

            Dispatcher.BeginInvoke(new Action(async () =>
            {

                if (!Settings.CurrentSettings.ExecuteAssistantPython)
                {
                    try
                    {
                        await args.CallbackAsync("User disabled commands, they can be enabled again in the settings");
                    }
                    catch { }

                    return;
                }



                string output = string.Empty;
                try
                {
                    string code = args.GetValue<string>();
                    var commandUI = new AssistantExecutedPython()
                    {
                        Code = code,
                    };
                    icAssistantContent.Items.Add(commandUI);

                    AdjustAssistantMessageSpacing();
                    scvAssistant.ScrollToBottom();

                    if (Settings.CurrentSettings.ExecuteAssistantCommands)
                    {
                        try
                        {
                            Trace.WriteLine("Executing Python Code:\n" + code);//Debugging
                            output = RunPythonAndGetOutput(code);
                            Trace.WriteLine("Python Output\n" + output);

                            if (!string.IsNullOrEmpty(output))
                            {
                                //update output in the UI
                                commandUI.SetOuput(output.TrimEnd(new char[] { '\n', '\r' }));
                            }


                            await args.CallbackAsync(output);//Debugging

                        }
                        catch (Exception ex)
                        {
                            //raise ex
                            Trace.WriteLine("Error running python code\n", ex.ToString());

                            try
                            {
                                await args.CallbackAsync(ex.Message);
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        try
                        {
                            await args.CallbackAsync("User disabled python scripting, they can be enabled again in the settings");
                        }
                        catch { }
                    }
                }
                catch { }
            }));
        }
    }
}
