// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace Files.App.ViewModels.Dialogs
{
	public sealed partial class BulkRenameItemViewModel : ObservableObject
	{
		public ListedItem SourceItem { get; }
		public string OriginalPath { get; }
		public string ParentDirectory { get; }
		public FilesystemItemType ItemType { get; }
		public bool IsDirectory { get; }
		public int RowNumber { get; }
		public string RowNumberText { get; }
		public BitmapImage? FileImage { get; }

		public string OriginalFullName { get; }
		public string OriginalStem { get; }
		public string OriginalExtension { get; }

		public Visibility ImageVisibility => FileImage is not null ? Visibility.Visible : Visibility.Collapsed;
		public Visibility FallbackGlyphVisibility => FileImage is null ? Visibility.Visible : Visibility.Collapsed;
		public string FallbackGlyph => IsDirectory ? "\uE8B7" : "\uE8A5";

		private string _editableText = string.Empty;
		public string EditableText => _editableText;

		private string _suffixExtension = string.Empty;
		public string SuffixExtension => _suffixExtension;

		public string FullResultName => _editableText + _suffixExtension;

		private int _anchorIndex;
		public int AnchorIndex => _anchorIndex;

		private int _caretIndex;
		public int CaretIndex => _caretIndex;

		private bool _isCursorActive = true;
		public bool IsCursorActive => _isCursorActive;

		private bool _hasError;
		public bool HasError => _hasError;

		private string _errorMessage = string.Empty;
		public string ErrorMessage => _errorMessage;

		public bool IsModified => !string.Equals(FullResultName, OriginalFullName, StringComparison.Ordinal);

		public int SelectionStart => Math.Min(_anchorIndex, _caretIndex);
		public int SelectionEnd => Math.Max(_anchorIndex, _caretIndex);
		public bool HasSelection => _isCursorActive && _anchorIndex != _caretIndex;

		private string _beforeSelectionText = string.Empty;
		public string BeforeSelectionText => _beforeSelectionText;

		private string _selectedText = string.Empty;
		public string SelectedText => _selectedText;

		private string _afterSelectionText = string.Empty;
		public string AfterSelectionText => _afterSelectionText;

		private Visibility _selectionVisibility = Visibility.Collapsed;
		public Visibility SelectionVisibility => _selectionVisibility;

		private Visibility _caretStartVisibility = Visibility.Collapsed;
		public Visibility CaretStartVisibility => _caretStartVisibility;

		private Visibility _caretEndVisibility = Visibility.Collapsed;
		public Visibility CaretEndVisibility => _caretEndVisibility;

		public Visibility SuffixVisibility => string.IsNullOrEmpty(_suffixExtension) ? Visibility.Collapsed : Visibility.Visible;
		public Visibility ErrorVisibility => _hasError ? Visibility.Visible : Visibility.Collapsed;
		public Visibility ModifiedIndicatorVisibility => !_hasError && IsModified ? Visibility.Visible : Visibility.Collapsed;
		public double RowOpacity => _isCursorActive ? 1.0 : 0.55;
		public string RowToolTip => _hasError
			? _errorMessage
			: (IsModified ? $"{OriginalFullName} → {FullResultName}" : OriginalFullName);

		public BulkRenameItemViewModel(ListedItem item, int rowNumber)
		{
			SourceItem = item;
			RowNumber = rowNumber;
			RowNumberText = rowNumber.ToString();

			OriginalPath = item.GetRequiredPath();
			ParentDirectory = SystemIO.Path.GetDirectoryName(OriginalPath) ?? string.Empty;
			IsDirectory = item.PrimaryItemAttribute == StorageItemTypes.Folder;
			ItemType = IsDirectory ? FilesystemItemType.Directory : FilesystemItemType.File;
			FileImage = item.FileImage;

			var fileName = SystemIO.Path.GetFileName(OriginalPath);
			OriginalFullName = !string.IsNullOrEmpty(fileName)
				? fileName
				: (item.ItemNameRaw ?? item.Name ?? string.Empty);

			(OriginalStem, OriginalExtension) = SplitNameAndExtension(OriginalFullName, IsDirectory);

			ApplyState(OriginalStem, OriginalExtension, 0, OriginalStem.Length, isCursorActive: true);
		}

		public static (string Stem, string Extension) SplitNameAndExtension(string fullName, bool isDirectory)
		{
			if (isDirectory || string.IsNullOrEmpty(fullName))
				return (fullName, string.Empty);

			int lastDot = fullName.LastIndexOf('.');
			if (lastDot > 0 && lastDot < fullName.Length - 1)
				return (fullName[..lastDot], fullName[lastDot..]);

			return (fullName, string.Empty);
		}

		public void ApplyState(string editableText, string suffixExtension, int anchorIndex, int caretIndex, bool isCursorActive)
		{
			var clampedAnchor = Math.Clamp(anchorIndex, 0, editableText.Length);
			var clampedCaret = Math.Clamp(caretIndex, 0, editableText.Length);

			var oldFull = FullResultName;
			var oldSuffix = _suffixExtension;
			var oldActive = _isCursorActive;
			var oldModified = IsModified;

			_editableText = editableText;
			_suffixExtension = suffixExtension;
			_anchorIndex = clampedAnchor;
			_caretIndex = clampedCaret;
			_isCursorActive = isCursorActive;

			int selStart = Math.Min(_anchorIndex, _caretIndex);
			int selEnd = Math.Max(_anchorIndex, _caretIndex);
			bool hasSel = _isCursorActive && selStart != selEnd;

			var newBefore = _editableText[..selStart];
			var newSelected = hasSel ? _editableText[selStart..selEnd] : string.Empty;
			var newAfter = _editableText[selEnd..];
			var newSelVis = hasSel ? Visibility.Visible : Visibility.Collapsed;
			var newCaretStartVis = _isCursorActive && _caretIndex == selStart ? Visibility.Visible : Visibility.Collapsed;
			var newCaretEndVis = hasSel && _caretIndex == selEnd ? Visibility.Visible : Visibility.Collapsed;

			if (!string.Equals(_beforeSelectionText, newBefore, StringComparison.Ordinal))
			{
				_beforeSelectionText = newBefore;
				OnPropertyChanged(nameof(BeforeSelectionText));
			}

			if (!string.Equals(_selectedText, newSelected, StringComparison.Ordinal))
			{
				_selectedText = newSelected;
				OnPropertyChanged(nameof(SelectedText));
			}

			if (!string.Equals(_afterSelectionText, newAfter, StringComparison.Ordinal))
			{
				_afterSelectionText = newAfter;
				OnPropertyChanged(nameof(AfterSelectionText));
			}

			if (_selectionVisibility != newSelVis)
			{
				_selectionVisibility = newSelVis;
				OnPropertyChanged(nameof(SelectionVisibility));
			}

			if (_caretStartVisibility != newCaretStartVis)
			{
				_caretStartVisibility = newCaretStartVis;
				OnPropertyChanged(nameof(CaretStartVisibility));
			}

			if (_caretEndVisibility != newCaretEndVis)
			{
				_caretEndVisibility = newCaretEndVis;
				OnPropertyChanged(nameof(CaretEndVisibility));
			}

			if (!string.Equals(oldSuffix, _suffixExtension, StringComparison.Ordinal))
			{
				OnPropertyChanged(nameof(SuffixExtension));
				OnPropertyChanged(nameof(SuffixVisibility));
			}

			if (oldActive != _isCursorActive)
			{
				OnPropertyChanged(nameof(IsCursorActive));
				OnPropertyChanged(nameof(RowOpacity));
			}

			if (!string.Equals(oldFull, FullResultName, StringComparison.Ordinal))
			{
				OnPropertyChanged(nameof(EditableText));
				OnPropertyChanged(nameof(FullResultName));
				OnPropertyChanged(nameof(RowToolTip));
			}

			if (oldModified != IsModified)
			{
				OnPropertyChanged(nameof(IsModified));
				OnPropertyChanged(nameof(ModifiedIndicatorVisibility));
				OnPropertyChanged(nameof(RowToolTip));
			}
		}

		public void SetValidation(bool hasError, string errorMessage)
		{
			if (_hasError == hasError && string.Equals(_errorMessage, errorMessage, StringComparison.Ordinal))
				return;

			_hasError = hasError;
			_errorMessage = errorMessage;

			OnPropertyChanged(nameof(HasError));
			OnPropertyChanged(nameof(ErrorMessage));
			OnPropertyChanged(nameof(ErrorVisibility));
			OnPropertyChanged(nameof(ModifiedIndicatorVisibility));
			OnPropertyChanged(nameof(RowToolTip));
		}
	}
}
