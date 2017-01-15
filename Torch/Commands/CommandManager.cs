﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NLog;
using Torch.API;
using Torch.Managers;
using VRage.Game.ModAPI;
using VRage.Network;

namespace Torch.Commands
{
    public class CommandManager
    {
        public char Prefix { get; set; }

        public CommandTree Commands { get; set; } = new CommandTree();
        private Logger _log = LogManager.GetLogger(nameof(CommandManager));
        private readonly ITorchBase _torch;
        private readonly ChatManager _chatManager = ChatManager.Instance;

        public CommandManager(ITorchBase torch, char prefix = '/')
        {
            _torch = torch;
            Prefix = prefix;
            _chatManager.MessageRecieved += HandleCommand;
            RegisterCommandModule(typeof(TorchCommands));
        }

        public bool HasPermission(ulong steamId, Command command)
        {
            _log.Warn("Command permissions not implemented");
            return true;
        }

        public bool IsCommand(string command)
        {
            return command.Length > 1 && command[0] == Prefix;
        }

        public void RegisterCommandModule(Type moduleType, ITorchPlugin plugin = null)
        {
            if (!moduleType.IsSubclassOf(typeof(CommandModule)))
                return;

            foreach (var method in moduleType.GetMethods())
            {
                var commandAttrib = method.GetCustomAttribute<CommandAttribute>();
                if (commandAttrib == null)
                    continue;

                var command = new Command(plugin, method);
                var cmdPath = string.Join(".", command.Path);
                _log.Info($"Registering command '{cmdPath}'");

                if (!Commands.AddCommand(command))
                    _log.Error($"Command path {cmdPath} is already registered.");
            }
        }

        public void RegisterPluginCommands(ITorchPlugin plugin)
        {
            var assembly = plugin.GetType().Assembly;
            foreach (var type in assembly.ExportedTypes)
            {
                RegisterCommandModule(type, plugin);
            }
        }

        public void HandleCommand(ChatMsg msg, ref bool sendToOthers)
        {
            if (msg.Text.Length < 1 || msg.Text[0] != Prefix)
                return;

            sendToOthers = false;

            var player = _torch.Multiplayer.GetPlayerBySteamId(msg.Author);
            if (player == null)
            {
                _log.Error($"Command {msg.Text} invoked by nonexistant player");
                return;
            }

            var cmdText = new string(msg.Text.Skip(1).ToArray());
            var split = Regex.Matches(cmdText, "(\"[^\"]+\"|\\S+)").Cast<Match>().Select(x => x.ToString().Replace("\"", "")).ToList();

            if (split.Count == 0)
                return;

            var command = Commands.ParseCommand(split, out List<string> args);

            if (command != null)
            {
                var cmdPath = string.Join(".", command.Path);

                if (!HasPermission(msg.Author, command))
                {
                    _log.Info($"{player.DisplayName} tried to use command {cmdPath} without permission");
                    return;
                }

                _log.Trace($"Invoking {cmdPath} for player {player.DisplayName}");
                var context = new CommandContext(_torch, command.Plugin, player, args);
                command.Invoke(context);
                _log.Info($"Player {player.DisplayName} ran command '{msg.Text}'");
            }
        }
    }
}