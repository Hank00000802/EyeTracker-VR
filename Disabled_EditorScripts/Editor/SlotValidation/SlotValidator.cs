using ithappy.Creative_Characters_FREE.CharacterCustomizationTool.Editor.Character;

namespace ithappy.Creative_Characters_FREE.CharacterCustomizationTool.Editor.SlotValidation
{
    public class SlotValidator
    {
        private readonly ISlotValidationRules[] _slotValidationRules =
        {
            new FullBodyToggledRule(),
            new SlotToggledRule(),
        };

        public void Validate(CustomizableCharacter character, SlotType type, bool isToggled)
        {
            foreach (var rule in _slotValidationRules)
            {
                rule.Validate(character, type, isToggled);
            }
        }
    }
}