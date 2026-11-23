namespace Starwright.Core;

public sealed class AboutSection
{
    public string id = "";
    public string title = "";
    public string[] paras = Array.Empty<string>();
}

/// <summary>
/// The About screen copy. The first five sections are the browser game's text (js/ui/about.js).
/// A sixth section says what this Godot build is, so the screen does not claim it still renders with three.js.
/// </summary>
public static class AboutContent
{
    public const string RepoUrl = "https://github.com/jsmith0475/Starwright";

    public static readonly AboutSection[] Sections =
    {
        new() { id = "what", title = "What Starwright is", paras = new[] {
            "Starwright is a rocket-building and orbital-flight game that runs in your browser. You bolt a rocket together in the assembly building, roll it to the pad, fly it to orbit with real orbital mechanics, and go on to the other worlds of its solar system.",
            "It is an original game with its own worlds, its own crew (the Pips), its own parts and its own art. It is free and open source, it runs on your own machine, and nobody can switch it off.",
        } },
        new() { id = "ksp1", title = "The game that started it: Kerbal Space Program", paras = new[] {
            "Starwright exists because of Kerbal Space Program. KSP began around 2010 as the idea of Felipe Falanghe, a developer at Squad, a media company in Mexico City that had never made a game. The first public version appeared in 2011: a small green crew, a box of rocket parts, and a planet to leave.",
            "It grew in the open for years, shaped by its players and by a huge modding community, and reached version 1.0 in April 2015. Along the way it did something no game had done at that scale: it made real orbital mechanics fun. Players learned, by failing cheerfully, why you burn sideways to stay up, what a transfer window is, and how hard it is to meet another craft in orbit. NASA and the European Space Agency both worked with its makers on missions for the game, and it turned up in classrooms and in the stories of people who went on to work in spaceflight.",
            "In 2017 the game was bought by Take-Two Interactive and published under its Private Division label. Two expansions followed, and the last major update, version 1.12, came in 2021. Kerbal Space Program is still on sale and still played today, and it is still wonderful. If you have never played it, you should.",
        } },
        new() { id = "ksp2", title = "Kerbal Space Program 2", paras = new[] {
            "A sequel was announced in August 2019, promising colonies, interstellar travel and multiplayer, with a release planned for 2020. Development moved to a new studio, Intercept Games, in 2020, and the date slipped several times.",
            "Kerbal Space Program 2 reached early access in February 2023 with much of what had been promised still to come. A science update followed at the end of that year. In 2024 the studio building it was closed, and the updates stopped with the game unfinished.",
            "For a lot of players that was the end of a long wait. Starwright is not going to bring KSP back, and it is not trying to replace it. It is one way to keep enjoying that same excitement of spaceflight and orbital dynamics, in a game that anyone can open, read and change.",
            "To the people who made Kerbal Space Program: thank you. This exists because of what you started.",
        } },
        new() { id = "made", title = "How Starwright was made", paras = new[] {
            "Starwright was not written by hand, and it would be wrong to let anyone think so.",
            "It is the result of an extensive collaboration between one person and teams of AI agents. Jerry Smith directed the project: what to build, what it should feel like, what was not good enough, and when it was ready. More than 20 Claude AI agents, working in parallel teams, wrote most of the code, the tests, the physics checks and the documents. A separate team of Grok AI agents, run by Jerry independently of the Claude agents, reviewed the work. Every build was played and judged by a human, and sent back when it fell short.",
            "The physics is checked against textbook orbital mechanics by its own test suite, and the Science entry on the main menu explains the mathematics the game flies by.",
            "\"Teams\" means teams of AI agents. Nobody at Anthropic or xAI, the companies that make Claude and Grok, worked on Starwright.",
        } },
        new() { id = "legal", title = "Independent and open", paras = new[] {
            "Starwright is an independent, original game. Its name, worlds, crew, parts, art, sound and code are its own: it uses no characters, art, music or code from any other game. It is not affiliated with or endorsed by the makers of Kerbal Space Program or by SpaceX, and it is not affiliated with or endorsed by Anthropic or xAI. Kerbal Space Program is a trademark of its owner and is named here only to tell its history.",
            "Starwright's code is released under the MIT License. Rendering uses three.js, also MIT licensed. The full source, the manual and the roadmap are at [github.com/jsmith0475/Starwright](" + RepoUrl + ").",
        } },
        new() { id = "port", title = "This build", paras = new[] {
            "This build is the C sharp port that runs in Godot 4.7. The browser game is still in this repository and still renders with three.js. The port keeps the same worlds, the same part catalog and the same orbital mechanics, and it is under the same MIT License. Copyright (c) 2026 Jerry Smith.",
            "The assembly building, the map, career mode and the full terrain of each world are not in this build yet. You can fly the stock rockets from the pad. The founder plays this build and decides when it is ready.",
        } },
    };
}
