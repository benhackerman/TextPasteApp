using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Functions;

/// <summary>Crear o editar una función (nombre, categoría, descripción y código).</summary>
public partial class FunctionEditorWindow : Window
{
    public CodeFunction Result { get; }

    public FunctionEditorWindow(CodeFunction function, IEnumerable<string> categories)
    {
        InitializeComponent();
        Result = function;
        NameBox.Text = function.Name;
        CategoryBox.ItemsSource = categories.Where(c => c.Length > 0).Distinct().OrderBy(c => c).ToList();
        CategoryBox.Text = function.Category;
        DescriptionBox.Text = function.Description;
        CodeBox.Text = function.Code;
        Loaded += (_, _) => (string.IsNullOrWhiteSpace(function.Name) ? NameBox : (Control)CodeBox).Focus();
    }

    private void Code_Changed(object sender, TextChangedEventArgs e)
    {
        var names = CodeTemplate.ParameterNames(CodeBox.Text);
        ParamsText.Text = names.Count == 0
            ? "Sin parámetros: se pega tal cual."
            : "Parámetros: " + string.Join(", ", names);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "Ponle un nombre para poder buscarla.", "TextPaste");
            NameBox.Focus();
            return;
        }
        Result.Name = NameBox.Text.Trim();
        Result.Category = CategoryBox.Text.Trim();
        Result.Description = DescriptionBox.Text.Trim();
        Result.Code = CodeBox.Text;
        DialogResult = true;
    }
}
