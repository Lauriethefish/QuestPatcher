using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using QuestPatcher.Views;

namespace QuestPatcher
{
    public class ButtonInfo
    {
        /// <summary>
        /// Text displayed on the button
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Value to complete the Task from opening the message box with.
        /// </summary>
        public bool ReturnValue { get; set; }

        /// <summary>
        /// Called open clicking the button.
        /// Set to null to do nothing
        /// </summary>
        public Action? OnClick { get; set; }

        /// <summary>
        /// Whether to close the dialogue when the button is clicked.
        /// If this is false, the task will not complete when this button is pressed
        /// </summary>
        public bool CloseDialogue { get; set; }

        /// <summary>
        /// Colour of the button
        /// </summary>
        public IBrush? Color { get; set; }
    }

    public class DialogBuilder
    {
        /// <summary>
        /// Used for the very important text.
        /// </summary>
        private static readonly Random Random = new();

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
            Text = Resources.Strings.Generic_OK,
            ReturnValue = true,
            CloseDialogue = true
        };

        public bool HideOkButton { get; set; }

        /// <summary>
        /// The default cancel button.
        /// </summary>
        public ButtonInfo CancelButton { get; set; } = new()
        {
            Text = Resources.Strings.Generic_Cancel,
            ReturnValue = false,
            CloseDialogue = true
        };

        public bool HideCancelButton { get; set; }

        private string? _stackTrace;

        private IEnumerable<ButtonInfo>? _extraButtons;

        /// <summary>
        /// Will display the stack trace of the exception within the dialogue
        /// </summary>
        /// <param name="ex">The exception to display</param>
        public void WithException(Exception ex)
        {
            _stackTrace = ex.ToString();
        }

        /// <summary>
        /// Sets the extra buttons for the dialog (other than OK and Cancel)
        /// </summary>
        /// <param name="buttons">The buttons to set</param>
        public void WithButtons(params ButtonInfo[] buttons)
        {
            _extraButtons = buttons;
        }

        /// <summary>
        /// Sets the extra buttons for the dialog (other than OK and Cancel)
        /// </summary>
        /// <param name="buttons">The buttons to set</param>
        public void WithButtons(IEnumerable<ButtonInfo> buttons)
        {
            _extraButtons = buttons;
        }

        /// <summary>
        /// Opens the dialogue.
        /// </summary>
        /// <param name="parentWindow">The window to prevent clicking while the dialogue is open</param>
        /// <param name="showLocation">Where to show the dialog on the screen</param>
        /// <returns>A task which will complete with false if the dialogue is closed, or any other return value specified inside your buttons when they are clicked</returns>
        public Task<bool> OpenDialogue(Window? parentWindow = null, WindowStartupLocation showLocation = WindowStartupLocation.CenterOwner)
        {
            MessageDialog dialogue = new();
            var messageText = dialogue.FindControl<TextBlock>("MessageText")!;
            messageText.Text = Text ?? "Placeholder Text";

            var titleText = dialogue.FindControl<TextBlock>("TitleText")!;
            titleText.Text = Title ?? "Placeholder Text";

            dialogue.Title = Title ?? "Placeholder Text";

            var stackTraceText = dialogue.FindControl<TextBox>("StackTraceText")!;
            if (_stackTrace == null)
            {
                stackTraceText.IsVisible = false;
            }
            else
            {
                stackTraceText.Text = _stackTrace;
            }

            var buttonsPanel = dialogue.FindControl<StackPanel>("ButtonsPanel")!;
            // Add the extra buttons if they have been set
            List<ButtonInfo> allButtons = _extraButtons != null ? new(_extraButtons) : new();

            // Cancel and OK buttons come first if they're enabled
            if (!HideCancelButton) { allButtons.Insert(0, CancelButton); }
            if (!HideOkButton) { allButtons.Insert(0, OkButton); }

            TaskCompletionSource<bool> completionSource = new();
            foreach (var buttonInfo in allButtons)
            {
                Button button = new()
                {
                    Content = buttonInfo.Text
                };
                if (buttonInfo.Color != null)
                {
                    button.Background = buttonInfo.Color;
                }

                button.Click += (_, _) =>
                {
                    buttonInfo.OnClick?.Invoke();

                    // Only buttons which close the dialogue complete the task
                    if (buttonInfo.CloseDialogue)
                    {
                        completionSource.SetResult(buttonInfo.ReturnValue);
                        dialogue.Close();
                    }
                };
                button.MinWidth = 100;
                button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;

                buttonsPanel.Children.Add(button);
            }

            // Make sure to only show the normal button pretty rarely
            var normalButton = dialogue.FindControl<Button>("NormalButton")!;
            if (Random.Next(50) == 0)
            {
                normalButton.IsVisible = true;
                normalButton.Click += (_, _) =>
                {
                    // Show the important facts window
                    Window factsWindow = new FactsWindow
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    factsWindow.ShowDialog(dialogue);
                };
            }

            dialogue.Closed += (_, _) =>
            {
                if (!completionSource.Task.IsCompleted)
                {
                    completionSource.SetResult(false);
                }
            };

            dialogue.WindowStartupLocation = showLocation;
            if (parentWindow != null)
            {
                dialogue.ShowDialog(parentWindow);
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
