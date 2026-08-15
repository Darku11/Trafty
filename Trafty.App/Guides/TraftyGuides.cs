namespace Trafty.App.Guides;

public static class TraftyGuides
{
    public static GuideProfile Archive { get; } = new(
        "Aelwyn",
        "Elven Archivist",
        "A",
        "#7D9A78",
        "Archives are only containers. Open them, search them, dissect them - the interesting part is what Mythic hid inside.",
        "Archive work requires patience and literacy. Two reasons the dwarves sent me.");

    public static GuideProfile WorldProps { get; } = new(
        "Brokk",
        "Dwarven Worldwright",
        "B",
        "#B57A45",
        "World props are the bones of a place: houses, trees, ruins, bridges, walls and all the things heroes eventually run into.",
        "If it stands upright, I built it. If it falls over, an elf named it.");

    public static GuideProfile Zone { get; } = new(
        "Corvin",
        "Breton Cartographer",
        "C",
        "#A66D5D",
        "Fixtures, boundaries and terrain become a world only when their coordinates agree. Maps first; adventure follows.",
        "Place carefully. One tree in the wrong spot and players will call it a landmark for twenty years.");

    public static GuideProfile Ui { get; } = new(
        "Pipwick",
        "Lurikeen Tinkerer",
        "P",
        "#7B78A7",
        "Client UI files are tiny machines made from controls, coordinates and assumptions. Trafty should make them visible before you break them.",
        "Move one button and three others complain. Traditional interface craftsmanship.");

    public static GuideProfile Texture { get; } = new(
        "Liora",
        "Elven Texture Artisan",
        "L",
        "#8D6F91",
        "Textures give old geometry its identity. Preview them, replace them and keep the mip chain healthy.",
        "Geometry gets all the credit. Textures do all the lying.");

    public static GuideProfile Atmosphere { get; } = new(
        "Maelis",
        "Grove Alchemist",
        "M",
        "#6F8F70",
        "Color tables, light and weather decide whether a zone feels welcoming, cursed or suspiciously purple.",
        "Fog is simply mystery with excellent branding.");

    public static GuideProfile Audio { get; } = new(
        "Rurik",
        "Skald of Questionable Volume",
        "R",
        "#708695",
        "Sound sells impact. Inspect the client samples, learn where they live and make the world answer back.",
        "If it clangs, roars, whispers or explodes, I want a sample.");
}
