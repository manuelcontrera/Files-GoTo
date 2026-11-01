// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Files.App.Data.Models;
using Files.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Files.App.Dialogs
{
	public sealed partial class GoToFolderDialog : ContentDialog
	{
		private readonly IVisitedFoldersService _visitedService;
		public string? SelectedFolderPath { get; private set; }
		private readonly ObservableCollection<VisitedFolderItem> _items = new();

		public GoToFolderDialog(IVisitedFoldersService visitedService)
		{
			_visitedService = visitedService;
			InitializeComponent();
			FoldersListView.ItemsSource = _items;

			Loaded += (_, _) =>
			{
				SearchBox.Focus(FocusState.Programmatic);
				RefreshList(string.Empty);
			};
		}

		private void RefreshList(string query)
		{
			_items.Clear();
			var results = _visitedService.Search(query, 15);
			foreach (var item in results)
				_items.Add(item);

			if (_items.Count > 0)
				FoldersListView.SelectedIndex = 0;
		}

		private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			RefreshList(SearchBox.Text);
		}

		private void SearchBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (e.Key == VirtualKey.Down && _items.Count > 0)
			{
				FoldersListView.Focus(FocusState.Keyboard);
				FoldersListView.SelectedIndex = System.Math.Min(FoldersListView.SelectedIndex + 1, _items.Count - 1);
				if (FoldersListView.SelectedItem is not null)
					FoldersListView.ScrollIntoView(FoldersListView.SelectedItem);
				e.Handled = true;
			}
			else if (e.Key == VirtualKey.Up && _items.Count > 0)
			{
				FoldersListView.SelectedIndex = System.Math.Max(FoldersListView.SelectedIndex - 1, 0);
				if (FoldersListView.SelectedItem is not null)
					FoldersListView.ScrollIntoView(FoldersListView.SelectedItem);
				e.Handled = true;
			}
			else if (e.Key == VirtualKey.Enter && FoldersListView.SelectedItem is VisitedFolderItem item)
			{
				ConfirmSelection(item.FolderPath);
				e.Handled = true;
			}
		}

		private void FoldersListView_KeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (e.Key == VirtualKey.Enter && FoldersListView.SelectedItem is VisitedFolderItem item)
			{
				ConfirmSelection(item.FolderPath);
				e.Handled = true;
			}
		}

		private void FoldersListView_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (e.ClickedItem is VisitedFolderItem item)
				ConfirmSelection(item.FolderPath);
		}

		private void ConfirmSelection(string path)
		{
			SelectedFolderPath = path;
			Hide();
		}
	}
}
