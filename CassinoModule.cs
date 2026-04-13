
Skip to content
phsonhador1
Botzinho
Repository navigation
Code
Issues
Pull requests
Agents
Actions
Projects
Wiki
Security and quality
Insights
Settings
Important update
On April 24 we'll start using GitHub Copilot interaction data for AI model training unless you opt out. Review this update and manage your preferences in your GitHub account settings.
Files
Go to file
t
AdminControleModule.cs
AdminModule.cs
ApostaModule.cs
AutoRankService.cs
Botzinho.csproj
CassinoModule.cs
Dockerfile
EconomyModule.cs
HelpModule.cs
ModerationModule.cs
Program.cs
README.md
Botzinho
/
CassinoModule.cs
in
main

Edit

Preview
Indent mode

Spaces
Indent size

4
Line wrap mode

No wrap
Editing CassinoModule.cs file contents
1
2
3
4
5
6
7
8
9
10
11
12
13
14
15
16
17
18
19
20
21
22
23
24
25
26
27
28
29
30
31
32
33
34
35
36
using Discord;
using Discord.WebSocket;
using Botzinho.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using SkiaSharp;

namespace Botzinho.Cassino
{
    // --- CLASSES DO BLACKJACK VISUAL ---
    public class Card
    {
        public string Suit { get; set; } // P (Paus), O (Ouros), C (Copas), E (Espadas)
        public string Value { get; set; } // 2-10, J, Q, K, A
        public int Score { get; set; } // 2-10, J,Q,K = 10, A = 1 ou 11

        // Nome do arquivo de imagem, ex: "k_spades.png"
        public string ImagePath => $"{Value.ToLower()}_{GetFullSuitName()}.png";

        private string GetFullSuitName()
        {
            return Suit switch { "P" => "clubs", "O" => "diamonds", "C" => "hearts", "E" => "spades", _ => "" };
        }
    }

    public static class BlackjackLogic
    {
        public static List<Card> CreateDeck()
        {
            string[] suits = { "P", "O", "C", "E" };
            string[] values = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "j", "q", "k", "a" };
            var deck = new List<Card>();

Use Control + Shift + m to toggle the tab key moving focus. Alternatively, use esc then tab to move to the next interactive element on the page.
 
