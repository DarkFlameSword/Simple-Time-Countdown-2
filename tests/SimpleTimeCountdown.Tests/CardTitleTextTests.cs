using System.Windows;
using System.Windows.Documents;
using TimeCountdown.Controls;
using Xunit;
using Size = System.Windows.Size;

namespace TimeCountdown.Tests;

public sealed class CardTitleTextTests
{
    private const double CardWidth = 360;

    [Fact]
    public void AShortTitleKeepsTheFullSize()
    {
        Sta.Run(() =>
        {
            var title = Measure("Renew passport");

            Assert.Equal(24, title.FontSize);
        });
    }

    [Fact]
    public void ALongerTitleStepsDownInsteadOfBeingCutOff()
    {
        Sta.Run(() =>
        {
            var title = Measure("Submit the final thesis draft to the graduate research office before the committee meeting");

            Assert.InRange(title.FontSize, 17, 23);
            Assert.True(title.DesiredSize.Height <= 2 * title.LineHeight + 1, "should fit in two lines at the smaller size");
        });
    }

    [Fact]
    public void ATitleTooLongForTwoLinesWrapsInFullAtTheSmallestSize()
    {
        Sta.Run(() =>
        {
            var text = string.Concat(Enumerable.Repeat("完成毕业论文终稿并提交给研究生院审核委员会进行最终审查", 4));
            var title = Measure(text);

            Assert.Equal(17, title.FontSize);
            Assert.True(title.DesiredSize.Height > 2 * title.LineHeight, "should wrap onto more lines");
            Assert.True(double.IsInfinity(title.MaxHeight));
        });
    }

    private static CardTitleText Measure(string text)
    {
        var title = new CardTitleText { Text = text, MaxFontSize = 24, MinFontSize = 17 };
        TextElement.SetFontFamily(title, new System.Windows.Media.FontFamily("Cambria, Microsoft YaHei UI"));
        title.Measure(new Size(CardWidth, double.PositiveInfinity));
        return title;
    }
}
