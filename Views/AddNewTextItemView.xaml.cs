using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using TextPasteApp.ViewModels;


namespace TextPasteApp.Views
{
    public partial class AddNewTextItemView : Window
    {
        public AddNewTextItemView(AddNewTextItemViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}