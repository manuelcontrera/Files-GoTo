// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using Files.App.Data.Commands;
using Files.App.Data.Contexts;
using Files.App.Dialogs;
using Files.App.Services;
using Microsoft.UI.Xaml;

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class GoToFolderAction : ObservableObject, IAction
	{
		private readonly IVisitedFoldersService _visitedService;
		private readonly IContentPageContext _context;

		public string Label
		{
			get
			{
				var localized = Strings.Command_GoToFolder_Label.GetLocalizedResource();
				return string.IsNullOrEmpty(localized) ? "Ir a carpeta..." : localized;
			}
		}

		public string Description
		{
			get
			{
				var localized = Strings.Command_GoToFolder_Description.GetLocalizedResource();
				return string.IsNullOrEmpty(localized) ? "Abre un selector rápido para saltar inmediatamente a cualquier carpeta visitada" : localized;
			}
		}

		public ActionCategory Category => ActionCategory.Navigation;

		public RichGlyph Glyph => new("\uED43");

		// Atajo por defecto: Ctrl + G (Modificable en Ajustes > Acciones)
		public HotKey HotKey => new(Keys.G, KeyModifiers.Ctrl);

		public bool IsExecutable => true;

		public GoToFolderAction()
		{
			_visitedService = Ioc.Default.GetRequiredService<IVisitedFoldersService>();
			_context = Ioc.Default.GetRequiredService<IContentPageContext>();
		}

		public async Task ExecuteAsync(object? parameter = null)
		{
			var dialog = new GoToFolderDialog(_visitedService)
			{
				XamlRoot = MainWindow.Instance.Content.XamlRoot
			};

			await dialog.ShowAsync();

			if (!string.IsNullOrWhiteSpace(dialog.SelectedFolderPath) && Directory.Exists(dialog.SelectedFolderPath))
			{
				// Navegar en la pestaña / shell activo
				_context.ShellPage?.NavigateToPath(dialog.SelectedFolderPath);
			}
		}
	}
}
