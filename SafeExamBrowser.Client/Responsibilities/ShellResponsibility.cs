/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System.ComponentModel;
using System.Linq;
using SafeExamBrowser.Configuration.Contracts.Cryptography;
using SafeExamBrowser.I18n.Contracts;
using SafeExamBrowser.Logging.Contracts;
using SafeExamBrowser.UserInterface.Contracts;
using SafeExamBrowser.UserInterface.Contracts.MessageBox;
using SafeExamBrowser.UserInterface.Contracts.Shell;

namespace SafeExamBrowser.Client.Responsibilities
{
	internal class ShellResponsibility : ClientResponsibility
	{
		private readonly IActionCenter actionCenter;
		private readonly IHashAlgorithm hashAlgorithm;
		private readonly IMessageBox messageBox;
		private readonly ITaskbar taskbar;
		private readonly IUserInterfaceFactory uiFactory;

		public ShellResponsibility(
			IActionCenter actionCenter,
			ClientContext context,
			IHashAlgorithm hashAlgorithm,
			ILogger logger,
			IMessageBox messageBox,
			ITaskbar taskbar,
			IUserInterfaceFactory uiFactory) : base(context, logger)
		{
			this.actionCenter = actionCenter;
			this.hashAlgorithm = hashAlgorithm;
			this.messageBox = messageBox;
			this.taskbar = taskbar;
			this.uiFactory = uiFactory;
		}

		public override void Assume(ClientTask task)
		{
			switch (task)
			{
				case ClientTask.CloseShell:
					CloseShell();
					break;
				case ClientTask.DeregisterEvents:
					DeregisterEvents();
					break;
				case ClientTask.RegisterEvents:
					RegisterEvents();
					break;
				case ClientTask.ShowShell:
					ShowShell();
					break;
			}
		}

		private void DeregisterEvents()
		{
			actionCenter.QuitButtonClicked -= Shell_QuitButtonClicked;
			taskbar.LoseFocusRequested -= Taskbar_LoseFocusRequested;
			taskbar.QuitButtonClicked -= Shell_QuitButtonClicked;

			foreach (var activator in Context.Activators.OfType<ITerminationActivator>())
			{
				activator.Activated -= TerminationActivator_Activated;
			}
		}

		private void RegisterEvents()
		{
			actionCenter.QuitButtonClicked += Shell_QuitButtonClicked;
			taskbar.LoseFocusRequested += Taskbar_LoseFocusRequested;
			taskbar.QuitButtonClicked += Shell_QuitButtonClicked;

			foreach (var activator in Context.Activators.OfType<ITerminationActivator>())
			{
				activator.Activated += TerminationActivator_Activated;
			}
		}

		private void CloseShell()
		{
			if (Settings?.UserInterface.ActionCenter.EnableActionCenter == true)
			{
				actionCenter.Close();
			}

			if (Settings?.UserInterface.Taskbar.EnableTaskbar == true)
			{
				taskbar.Close();
			}
		}

		private void ShowShell()
		{
			if (Settings.UserInterface.ActionCenter.EnableActionCenter)
			{
				actionCenter.Promote();
			}

			if (Settings.UserInterface.Taskbar.EnableTaskbar)
			{
				taskbar.Show();
			}
		}

		private void Shell_QuitButtonClicked(CancelEventArgs args)
		{
			PauseActivators();
			args.Cancel = !TryInitiateShutdown();
			ResumeActivators();
		}

		private void Taskbar_LoseFocusRequested(bool forward)
		{
			Context.Browser.Focus(forward);
		}

		private void TerminationActivator_Activated()
		{
			PauseActivators();
			TryInitiateShutdown();
			ResumeActivators();
		}

		private bool TryInitiateShutdown()
		{
			var initiateShutdown = HasQuitPassword ? TryValidateQuitPassword() : TryConfirmShutdown();
			var success = false;

			if (initiateShutdown)
			{
				success = TryRequestShutdown();
			}

			return success;
		}

		private bool TryConfirmShutdown()
		{
			var result = messageBox.Show(TextKey.MessageBox_Quit, TextKey.MessageBox_QuitTitle, MessageBoxAction.YesNo, MessageBoxIcon.Question);
			var quit = result == MessageBoxResult.Yes;

			if (quit)
			{
				Logger.Info("The user chose to terminate the application.");
			}

			return quit;
		}

		private bool TryValidateQuitPassword()
		{
			var dialog = uiFactory.CreatePasswordDialog(TextKey.PasswordDialog_QuitPasswordRequired, TextKey.PasswordDialog_QuitPasswordRequiredTitle);
			var result = dialog.Show();
			var success = false;

			if (!result.Success)
			{
				return false;
			}

			switch (VerifyQuitPassword(result.Password))
			{
				case QuitPasswordVerification.Valid:
					success = true;
					Logger.Info("The user entered the correct quit password, the application will now terminate.");
					break;
				case QuitPasswordVerification.Expired:
					Logger.Info("The quit password has expired.");
					messageBox.Show(TextKey.MessageBox_QuitPasswordExpired, TextKey.MessageBox_QuitPasswordExpiredTitle, icon: MessageBoxIcon.Warning);
					break;
				case QuitPasswordVerification.Unavailable:
					Logger.Info("The quit password could not be verified because no CBT kiosk endpoint was reachable.");
					messageBox.Show(TextKey.MessageBox_QuitPasswordUnavailable, TextKey.MessageBox_QuitPasswordUnavailableTitle, icon: MessageBoxIcon.Warning);
					break;
				default:
					Logger.Info("The user entered the wrong quit password.");
					messageBox.Show(TextKey.MessageBox_InvalidQuitPassword, TextKey.MessageBox_InvalidQuitPasswordTitle, icon: MessageBoxIcon.Warning);
					break;
			}

			return success;
		}
	}
}
