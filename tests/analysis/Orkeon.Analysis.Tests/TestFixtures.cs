using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests;

internal static class TestFixtures
{
    /// <summary>Default virtual root used by tests that mount a temp directory.</summary>
    public const string TestVirtualRoot = "/src";

    /// <summary>Creates a disk-backed <see cref="IFileSystemService"/> mounted at <see cref="TestVirtualRoot"/>.</summary>
    public static IFileSystemService CreateFs(string physicalDir, string virtualRoot = TestVirtualRoot) =>
        new DiskBackedFileSystemService(physicalDir, virtualRoot);


    public const string SimpleClass = """
        /** Simple domain service. */
        export class UserService {
            public create(name: string): void {
            }
            private helper(): number {
                return 1;
            }
        }

        export interface IUserService {
            create(name: string): void;
        }
        """;

    public const string Imports = """
        import { Foo } from './utils';
        import * as lodash from 'lodash';
        import type { Bar } from '@scope/pkg';

        export function run(): Foo {
            return new Foo();
        }
        """;

    public const string Utils = """
        export class Foo {
            bark(): string {
                return 'bark';
            }
        }
        """;

    public const string Inheritance = """
        export interface IClient {
            send(): void;
        }

        export class Base {
            protected ping(): void {
            }
        }

        export class Derived extends Base implements IClient {
            send(): void {
                this.ping();
            }
        }
        """;

    public const string Calls = """
        export class A {
            private b: B = new B();
            run(): number {
                return this.b.compute() + this.helper();
            }
            private helper(): number {
                return 42;
            }
        }

        export class B {
            compute(): number {
                return 10;
            }
        }
        """;

    public const string BrokenSyntax = """
        export class Broken {
            run(): void {
                const value = compute(
        }
        """;

    public const string Empty = "";

    public const string Decorators = """
        function Injectable(): ClassDecorator {
            return (target) => target;
        }

        @Injectable()
        export class Service {
            @Deprecated()
            public run(): void {
            }
        }
        """;

    public const string PackageJson = """
        { "name": "test-pkg", "version": "1.0.0" }
        """;

    public const string Gitignore = """
        dist/
        """;

    public static async Task<string> WriteDirectoryAsync(
        (string RelativePath, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "raggable-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (relative, content) in files)
        {
            var full = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            var parent = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            await File.WriteAllTextAsync(full, content);
        }
        return dir;
    }

    public const string PythonSimpleClass = "from abc import ABC, abstractmethod\n\n"
        + "MAX_RETRIES = 3\n"
        + "debug_mode = True\n\n"
        + "class UserService:\n"
        + "    \"\"\"Service for user operations.\"\"\"\n\n"
        + "    def __init__(self, db):\n"
        + "        self.db = db\n\n"
        + "    @property\n"
        + "    def connection(self):\n"
        + "        return self.db\n\n"
        + "    def create(self, name: str) -> int:\n"
        + "        \"\"\"Create a user and return id.\"\"\"\n"
        + "        return 1\n\n"
        + "    def __del__(self):\n"
        + "        pass\n\n"
        + "    def __eq__(self, other):\n"
        + "        return True\n\n"
        + "class IRepository(ABC):\n"
        + "    @abstractmethod\n"
        + "    def find(self, id):\n"
        + "        pass\n\n"
        + "def helper(x):\n"
        + "    return x\n";

    public const string PythonFastApi = "from fastapi import FastAPI, Depends\n\n"
        + "app = FastAPI()\n\n"
        + "@app.get(\"/items\")\n"
        + "def list_items():\n"
        + "    return []\n\n"
        + "@app.post(\"/items\")\n"
        + "def create_item(item: dict):\n"
        + "    return item\n";

    public const string PythonRelativeImport = """
        from .utils import helper
        from ..shared import tools

        def main():
            helper()
        """;

    public const string CSharpController = """
        using System;
        using Microsoft.AspNetCore.Mvc;

        namespace Example.Api
        {
            /// <summary>
            /// Sample API controller.
            /// </summary>
            [ApiController]
            [Route("api/[controller]")]
            public class ItemsController : ControllerBase
            {
                /// <summary>Gets all items.</summary>
                [HttpGet]
                public IActionResult GetAll() => Ok();

                [HttpPost]
                [Authorize]
                public IActionResult Create([FromBody] string name) => Ok();
            }

            public record ItemDto(int Id, string Name);

            public struct Point { public int X; public int Y; }

            public interface IItems { void Do(); }

            public enum Status { Active, Inactive }
        }
        """;

    public const string GoService = """
        package example

        import "fmt"

        // Server handles requests.
        type Server struct {
            Port int
            name string
        }

        // Start launches the server.
        func (s *Server) Start() error {
            return nil
        }

        func (s *Server) private() {}

        type Handler interface {
            Handle()
        }

        const MaxConnections = 100

        func main() {
            fmt.Println("hi")
        }
        """;

    public const string RustLib = """
        use std::fmt;

        /// A point in 2D space.
        #[derive(Debug, Clone)]
        pub struct Point {
            pub x: f64,
            pub y: f64,
        }

        pub trait Drawable {
            fn draw(&self);
        }

        impl Drawable for Point {
            fn draw(&self) {}
        }

        impl Point {
            pub fn new(x: f64, y: f64) -> Self {
                Point { x, y }
            }
        }

        pub(crate) fn internal_helper() {}

        const MAX: u32 = 100;

        mod tests {
            fn case() {}
        }
        """;

    public const string NestJsController = """
        import { Controller, Get, Post, Injectable } from '@nestjs/common';

        @Controller('users')
        export class UsersController {
            @Get()
            list() { return []; }

            @Post()
            create() { return {}; }
        }

        @Injectable()
        export class UsersService {
            find() { return null; }
        }
        """;

    public const string AngularComponent = """
        import { Component, Input, Output, EventEmitter } from '@angular/core';

        @Component({ selector: 'app-item', template: '' })
        export class ItemComponent {
            @Input() name: string = '';
            @Output() changed = new EventEmitter<string>();
        }
        """;

    public const string LinearPipelineTs = """
        export class Pipeline {
            run(input: string): string {
                const a = input.trim();
                const b = a.toUpperCase();
                const c = b.replace('X', 'Y');
                return c;
            }
        }
        """;

    public const string ComplexMethodTs = """
        export class Handler {
            async process(data: string): Promise<number> {
                let count = 0;
                if (data.length > 0) {
                    count = data.length;
                } else {
                    count = -1;
                }
                try {
                    for (const ch of data) {
                        await this.emit(ch);
                    }
                } catch (e) {
                    count = 0;
                }
                return count;
            }
            async emit(_s: string): Promise<void> {}
        }
        """;

    public const string ActorProcessTs = """
        export class Actor {
            _process(event: any): void {
                const logic = this.getLogic();
                if (!logic) {
                    return;
                }
                try {
                    const next = logic.transition(this.state, event);
                    this.state = next;
                    this.emit(next);
                } catch (err) {
                    this.onError(err);
                }
            }
            getLogic(): any { return null; }
            emit(_s: any): void {}
            onError(_e: any): void {}
            state: any = {};
        }
        """;

    public const string GetInitialSnapshotTs = """
        export class SnapshotMaker {
            getInitialSnapshot(logic: any, input: any): any {
                const initEvent = { type: 'init', input };
                const machine = logic.createMachine();
                const state = machine.initialState;
                const value = state.value;
                const context = state.context;
                return { initEvent, machine, state, value, context };
            }
        }
        """;

    public const string BrokenBodyTs = """
        export class Broken {
            run(): number {
                const a = 1;
                const b = compute(
                return a + b;
            }
        }
        """;

    public static string CreateLargeTs(int symbolCount = 200)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < symbolCount; i++)
        {
            sb.AppendLine($"export function fn{i}(): number {{ return {i}; }}");
        }
        return sb.ToString();
    }

    public const string JsLib = """
        import { readFile } from './io.js';
        const path = require('path');

        /** Greets a person. */
        export function greet(name) {
            return `hi ${name}`;
        }

        export const shout = (text) => text.toUpperCase();

        class Animal {
            constructor(name) { this.name = name; }
            speak() { return greet(this.name); }
        }

        export default class Dog extends Animal {
            static create() { return new Dog('rex'); }
            async fetch() {
                for (const item of [1, 2]) {
                    if (item > 1) { await readFile(path.join('a', 'b')); }
                }
                try { this.speak(); } catch (e) { throw e; }
            }
        }
        """;

    public const string JavaLib = """
        package com.example.shapes;

        import java.util.List;

        /** A shape with an area. */
        public interface Shape {
            double area();
        }

        public enum Color { RED, GREEN }

        @Deprecated
        public class Circle extends Base implements Shape {
            private static final double PI = 3.14;
            private final double radius;

            public Circle(double radius) { this.radius = radius; }

            @Override
            public double area() {
                double total = 0;
                for (int i = 0; i < 2; i++) {
                    if (radius > 0) { total += PI * radius * radius; }
                }
                try { List.of(1).size(); } catch (RuntimeException e) { throw e; }
                return total;
            }
        }

        public record Point(int x, int y) {}
        """;

    public const string CLib = """
        #include <stdio.h>
        #include "util.h"

        #define MAX 10

        /* A point in 2D space. */
        struct point {
            int x;
            int y;
        };

        typedef struct point point_t;

        enum color { RED, GREEN };

        static int helper(int a);

        /** Adds two numbers. */
        int add(int a, int b) {
            int total = a + b;
            for (int i = 0; i < MAX; i++) {
                if (total > MAX) { return MAX; }
            }
            printf("%d", total);
            return total;
        }
        """;

    public const string CppLib = """
        #include <vector>
        #include "shape.hpp"

        namespace geo {

        /// A drawable shape.
        class Shape {
        public:
            virtual ~Shape() = default;
            virtual double area() const = 0;
        };

        struct Circle : public Shape {
            explicit Circle(double r) : r_(r) {}
            double area() const override { return 3.14 * r_ * r_; }
        private:
            double r_;
        };

        template <typename T>
        T twice(T value) {
            return value * 2;
        }

        double Circle::perimeter() const {
            for (auto v : std::vector<int>{1, 2}) {
                if (v > 1) { return twice(r_); }
            }
            try { throw 1; } catch (int e) { return 0; }
        }

        }
        """;

    public const string PhpLib = """
        <?php
        namespace App\Models;

        use App\Contracts\Shape;

        /** A circle. */
        #[Entity]
        class Circle extends Base implements Shape
        {
            const PI = 3.14;
            private float $radius;

            public function __construct(float $radius) { $this->radius = $radius; }

            public function area(): float
            {
                $total = 0;
                foreach ([1, 2] as $i) {
                    if ($i > 1) { $total += self::PI * $this->radius; }
                }
                try { strlen('x'); } catch (\Exception $e) { throw $e; }
                return $total;
            }
        }

        interface Named { public function name(): string; }

        trait Greets { public function greet() { return 'hi'; } }

        enum Suit { case Hearts; }

        function helper(int $a): int { return $a; }
        """;

    public const string RubyLib = """
        require 'json'
        require_relative 'util/io'

        # A shape module.
        module Shapes
          # A circle.
          class Circle < Base
            include Comparable
            PI = 3.14

            def initialize(radius)
              @radius = radius
            end

            def area
              total = 0
              [1, 2].each do |i|
                total += PI * @radius if i > 1
              end
              begin
                JSON.parse('{}')
              rescue StandardError => e
                raise e
              end
              total
            end

            def self.unit
              new(1)
            end
          end
        end

        def helper(a)
          a
        end
        """;

    public const string BashLib = """
        #!/usr/bin/env bash
        set -euo pipefail
        source ./lib/common.sh
        . "$HOME/env.sh"

        readonly MAX=10

        # Greets a person.
        greet() {
          local name="$1"
          if [ -z "$name" ]; then
            return 1
          fi
          for i in 1 2; do
            echo "hi $name $i"
          done
        }

        function cleanup {
          rm -f /tmp/x
        }

        greet world
        """;
}
