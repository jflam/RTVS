﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Microsoft.UnitTests.Core.FluentAssertions {
    [ExcludeFromCodeCoverage]
    public static class TaskAssertionsExtensions {
        public static TaskAssertions Should(this Task task) {
            return new TaskAssertions(task);
        }
    }
}