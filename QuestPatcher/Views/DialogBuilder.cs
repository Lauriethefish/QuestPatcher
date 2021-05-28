using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuestPatcher.Views
{
    public class ButtonInfo
    {
        /// <summary>
        /// Text displayed on the button
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Value to complete the Task<bool> from opening the message box with.
        /// </summary>
        public bool ReturnValue { get; set; } = false;

        /// <summary>
        /// Called open clicking the button.
        /// Set to null to do nothing
        /// </summary>
        public Action? OnClick { get; set; }

        /// <summary>
        /// Whether to close the dialogue when the button is clicked.
        /// If this is false, the task will not complete when this button is pressed
        /// </summary>
        public bool CloseDialogue { get; set; } = false;

        /// <summary>
        /// Colour of the button
        /// </summary>
        public IBrush? Color { get; set; }
    }

    public class DialogBuilder
    {
        /// <summary>
        /// Title of the dialogue window
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Text to display in the dialogue, underlined
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// The default OK button.
        /// </summary>
        public ButtonInfo OkButton { get; set; } = new()
        {
            Text = "OK",
            ReturnValue = true,
            CloseDialogue = true
        };

        public bool HideOkButton { get; set; } = false;

        /// <summary>
        /// The default cancel button.
        /// </summary>
        public ButtonInfo CancelButton { get; set; } = new()
        {
            Text = "Cancel",
            ReturnValue = false,
            CloseDialogue = true
        };

        public bool HideCancelButton { get; set; } = false;

        private string? _stackTrace;

        private ButtonInfo[]? _extraButtons;
        
        /// <summary>
        /// Will display the stack trace of the exception within the dialogue
        /// </summary>
        /// <param name="ex">The exception to display</param>
        public DialogBuilder WithException(Exception ex)
        {
            _stackTrace = ex.ToString();
            return this;
        }

        /// <summary>
        /// Adds extra buttons to the dialogue
        /// </summary>
        /// <param name="buttons">The buttons to add</param>
        public DialogBuilder WithButtons(params ButtonInfo[] buttons)
        {
            _extraButtons = buttons;
            return this;
        }

        /// <summary>
        /// Centers within inside window
        /// </summary>
        /// <param name="window">The window to move</param>
        /// <param name="within">The window to move to the center of</param>
        public static void CenterWindow(Window window, Window within)
        {
            double xOffset = (within.ClientSize.Width - window.ClientSize.Width) / 2.0;
            double yOffset = (within.ClientSize.Height - window.ClientSize.Height) / 2.0;

            window.Position = new PixelPoint(
                within.Position.X + (int) xOffset,
                within.Position.Y + (int) yOffset
            );
        }

        /// <summary>
        /// Opens the dialogue.
        /// </summary>
        /// <param name="parentWindow">The window to prevent clicking while the dialogue is open</param>
        /// <returns>A task which will complete with false if the dialogue is closed, or any other return value specified inside your buttons when they are clicked</returns>
        public Task<bool> OpenDialogue(Window? parentWindow = null, bool centerWithinWindow = true)
        {
            MessageDialog dialogue = new();
            TextBlock messageText = dialogue.FindControl<TextBlock>("messageText");
            messageText.Text = Text ?? "Placeholder Text";

            TextBlock titleText = dialogue.FindControl<TextBlock>("titleText");
            titleText.Text = Title ?? "Placeholder Text";

            dialogue.Title = Title ?? "Placeholder Text";

            TextBox stackTraceText = dialogue.FindControl<TextBox>("stackTraceText");
            if(_stackTrace == null)
            {
                stackTraceText.IsVisible = false;
            }
            else
            {
                stackTraceText.Text = _stackTrace;
            }

            StackPanel buttonsPanel = dialogue.FindControl<StackPanel>("buttonsPanel");
            // Add the extra buttons if they have been set
            List<ButtonInfo> allButtons = _extraButtons != null ? new(_extraButtons) : new();

            // Cancel and OK buttons come first if they're enabled
            if (!HideCancelButton) { allButtons.Insert(0, CancelButton); }
            if (!HideOkButton) { allButtons.Insert(0, OkButton); }

            TaskCompletionSource<bool> completionSource = new();
            foreach(ButtonInfo buttonInfo in allButtons) {
                Button button = new();
                button.Content = buttonInfo.Text;
                if(buttonInfo.Color != null)
                {
                    button.Background = buttonInfo.Color;
                }

                button.Click += (sender, args) =>
                {
                    buttonInfo.OnClick?.Invoke();

                    // Only buttons which close the dialogue complete the task
                    if(buttonInfo.CloseDialogue)
                    {
                        completionSource.SetResult(buttonInfo.ReturnValue);
                        dialogue.Close();
                    }
                };
                button.MinWidth = 100;
                button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;

                buttonsPanel.Children.Add(button);
            }

            dialogue.Closed += (sender, args) => 
            {
                if(!completionSource.Task.IsCompleted)
                {
                    completionSource.SetResult(false);
                }
            };

            if (parentWindow != null)
            {
                dialogue.IsVisible = false;
                dialogue.ShowDialog(parentWindow);
                if (centerWithinWindow)
                {
                    CenterWindow(dialogue, parentWindow);
                }
                dialogue.IsVisible = true;
            }
            else
            {
                dialogue.Show();
            }

            // This task will complete when a button is clicked that is set to complete the task
            return completionSource.Task;
        }
    }
}
