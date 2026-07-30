using NUnit.Framework;
using UnityEngine;

namespace Colloid.UitkFontFix.Tests
{
    /// <summary>Guards the pure language policy (ja/zh/ko).</summary>
    public class CjkLanguageTests
    {
        [TestCase(SystemLanguage.Japanese)]
        [TestCase(SystemLanguage.Chinese)]
        [TestCase(SystemLanguage.ChineseSimplified)]
        [TestCase(SystemLanguage.ChineseTraditional)]
        [TestCase(SystemLanguage.Korean)]
        public void ShouldPreferCjkUi_TrueForCjkLanguages(SystemLanguage language)
        {
            Assert.IsTrue(CjkLanguage.ShouldPreferCjkUi(language));
            Assert.IsTrue(FontFix.ShouldPreferCjkUi(language),
                "the FontFix facade must agree with CjkLanguage");
        }

        [TestCase(SystemLanguage.English)]
        [TestCase(SystemLanguage.French)]
        [TestCase(SystemLanguage.German)]
        [TestCase(SystemLanguage.Unknown)]
        public void ShouldPreferCjkUi_FalseForOtherLanguages(SystemLanguage language)
        {
            Assert.IsFalse(CjkLanguage.ShouldPreferCjkUi(language));
            Assert.IsFalse(FontFix.ShouldPreferCjkUi(language));
        }
    }
}
