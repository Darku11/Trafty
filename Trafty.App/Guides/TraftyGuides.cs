namespace Trafty.App.Guides;

public static class TraftyGuides
{
    public static GuideProfile Archive { get; } = new(
        "Aelwyn",
        "Elven Archivist",
        "A",
        "M12,2 L18,12 L12,22 L6,12 Z",
        "#7D9A78",
        "Archives are only containers. Open them, search them, dissect them - the interesting part is what Mythic hid inside.",
        "Archive work requires patience and literacy. Two reasons the dwarves sent me.");

    public static GuideProfile WorldProps { get; } = new(
        "Brokk",
        "Dwarven Worldwright",
        "B",
        "M4,8 L20,8 L20,11 L14,11 L14,14 L10,14 L10,11 L4,11 Z M9,15 L15,15 L15,17 L9,17 Z M10,18 L14,18 L14,20 L10,20 Z",
        "#B57A45",
        "World props are the bones of a place: houses, trees, ruins, bridges, walls and all the things heroes eventually run into.",
        "If it stands upright, I built it. If it falls over, an elf named it.");

    public static GuideProfile Zone { get; } = new(
        "Corvin",
        "Breton Cartographer",
        "C",
        "M12,2 L14,10 L22,12 L14,14 L12,22 L10,14 L2,12 L10,10 Z",
        "#A66D5D",
        "Fixtures, boundaries and terrain become a world only when their coordinates agree. Maps first; adventure follows.",
        "Place carefully. One tree in the wrong spot and players will call it a landmark for twenty years.");

    public static GuideProfile Ui { get; } = new(
        "Pipwick",
        "Lurikeen Tinkerer",
        "P",
        "M12,2 L13.5,6 L18,4 L16,8.5 L20,10 L16,11.5 L18,16 L13.5,14 L12,18 L10.5,14 L6,16 L8,11.5 L4,10 L8,8.5 L6,4 L10.5,6 Z",
        "#7B78A7",
        "Client UI files are tiny machines made from controls, coordinates and assumptions. Trafty should make them visible before you break them.",
        "Move one button and three others complain. Traditional interface craftsmanship.");

    public static GuideProfile Texture { get; } = new(
        "Liora",
        "Elven Texture Artisan",
        "L",
        "M12,2 L17,13 L17,16 L7,16 L7,13 Z",
        "#8D6F91",
        "Textures give old geometry its identity. Preview them, replace them and keep the mip chain healthy.",
        "Geometry gets all the credit. Textures do all the lying.");

    public static GuideProfile Atmosphere { get; } = new(
        "Maelis",
        "Grove Alchemist",
        "M",
        "M10,2 L14,2 L14,7 L19,19 L5,19 L10,7 Z",
        "#6F8F70",
        "Color tables, light and weather decide whether a zone feels welcoming, cursed or suspiciously purple.",
        "Fog is simply mystery with excellent branding.");

    public static GuideProfile Audio { get; } = new(
        "Rurik",
        "Skald of Questionable Volume",
        "R",
        "M4,4 L20,4 L14,20 L10,20 Z",
        "#708695",
        "Sound sells impact. Inspect the client samples, learn where they live and make the world answer back.",
        "If it clangs, roars, whispers or explodes, I want a sample.");
}
