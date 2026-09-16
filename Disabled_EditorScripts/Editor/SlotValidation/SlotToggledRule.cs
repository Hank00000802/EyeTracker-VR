using System.Linq;
using ithappy.Creative_Characters_FREE.CharacterCustomizationTool.Editor.Character;

namespace ithappy.Creative_Characters_FREE.CharacterCustomizationTool.Editor.SlotValidation
{
    public class SlotToggledRule : ISlotValidationRules
    {
        private readonly SlotType[] _slotExceptions =
        {
            SlotType.FullBody,
            SlotType.Body,
            SlotType.Faces,
        };

        public void Validate(CustomizableCharacter character, SlotType type, bool isToggled)
        {
            if (_slotExceptions.Contains(type) || !isToggled)
            {
                return;
            }

            character.Toggle(SlotType.FullBody, false);
        }
    }
}