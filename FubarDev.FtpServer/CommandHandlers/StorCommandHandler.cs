﻿//-----------------------------------------------------------------------
// <copyright file="StorCommandHandler.cs" company="Fubar Development Junker">
//     Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>
// <author>Mark Junker</author>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.FileSystem;

namespace FubarDev.FtpServer.CommandHandlers
{
    /// <summary>
    /// This class implements the STOR command (4.1.3.)
    /// </summary>
    public class StorCommandHandler : FtpCommandHandler
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StorCommandHandler"/> class.
        /// </summary>
        /// <param name="connection">The connection this command handler is created for</param>
        public StorCommandHandler(FtpConnection connection)
            : base(connection, "STOR")
        {
        }

        /// <inheritdoc/>
        public override bool IsAbortable => true;

        /// <inheritdoc/>
        public override async Task<FtpResponse> Process(FtpCommand command, CancellationToken cancellationToken)
        {
            if (!Data.TransferMode.IsBinary && Data.TransferMode.FileType != FtpFileType.Ascii)
                throw new NotSupportedException();

            var fileName = command.Argument;
            if (string.IsNullOrEmpty(fileName))
                return new FtpResponse(501, "No file name specified");

            var currentPath = Data.Path.Clone();
            var fileInfo = await Data.FileSystem.SearchFileAsync(currentPath, fileName, cancellationToken);
            if (fileInfo == null)
                return new FtpResponse(550, "Not a valid directory.");

            var doReplace = Data.RestartPosition.GetValueOrDefault() == 0 && fileInfo.Entry != null;

            await Connection.Write(new FtpResponse(150, "Opening connection for data transfer."), cancellationToken);
            using (var replySocket = await Connection.CreateResponseSocket())
            {
                replySocket.ReadStream.ReadTimeout = 10000;

                IBackgroundTransfer backgroundTransfer;
                if (doReplace)
                {
                    backgroundTransfer = await Data.FileSystem.ReplaceAsync(fileInfo.Entry, replySocket.ReadStream, cancellationToken);
                }
                else if (Data.RestartPosition.GetValueOrDefault() == 0 || fileInfo.Entry == null)
                {
                    backgroundTransfer = await Data.FileSystem.CreateAsync(fileInfo.Directory, fileInfo.FileName, replySocket.ReadStream, cancellationToken);
                }
                else
                {
                    backgroundTransfer = await Data.FileSystem.AppendAsync(fileInfo.Entry, Data.RestartPosition ?? 0, replySocket.ReadStream, cancellationToken);
                }
                if (backgroundTransfer != null)
                    Server.EnqueueBackgroundTransfer(backgroundTransfer, Connection);
            }

            return new FtpResponse(226, "Uploaded file successfully.");
        }
    }
}
