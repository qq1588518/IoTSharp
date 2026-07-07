//go:build cgo && (windows || linux)

package sonnetdb

import (
	"context"
	"database/sql"
	"database/sql/driver"
	"errors"
	"io"
)

func init() {
	sql.Register("sonnetdb", &Driver{})
}

// Driver implements database/sql/driver.Driver for SonnetDB.
type Driver struct {
}

// Open opens a database/sql connection. The name is the SonnetDB data directory.
func (d *Driver) Open(name string) (driver.Conn, error) {
	connection, err := Open(name)
	if err != nil {
		return nil, err
	}
	return &sqlConn{connection: connection}, nil
}

type sqlConn struct {
	connection *Connection
}

func (c *sqlConn) Prepare(query string) (driver.Stmt, error) {
	if _, err := c.connection.ensureOpen(); err != nil {
		return nil, err
	}
	return &sqlStmt{connection: c.connection, query: query}, nil
}

func (c *sqlConn) Close() error {
	return c.connection.Close()
}

func (c *sqlConn) Begin() (driver.Tx, error) {
	return nil, errors.New("sonnetdb: transactions are not supported")
}

func (c *sqlConn) Ping(ctx context.Context) error {
	if err := checkContext(ctx); err != nil {
		return err
	}
	_, err := c.connection.ensureOpen()
	return err
}

func (c *sqlConn) ExecContext(ctx context.Context, query string, args []driver.NamedValue) (driver.Result, error) {
	if err := checkContext(ctx); err != nil {
		return nil, err
	}
	if err := rejectNamedArgs(args); err != nil {
		return nil, err
	}
	affected, err := c.connection.ExecuteNonQuery(query)
	if err != nil {
		return nil, err
	}
	return sqlResult(affected), nil
}

func (c *sqlConn) QueryContext(ctx context.Context, query string, args []driver.NamedValue) (driver.Rows, error) {
	if err := checkContext(ctx); err != nil {
		return nil, err
	}
	if err := rejectNamedArgs(args); err != nil {
		return nil, err
	}
	result, err := c.connection.Execute(query)
	if err != nil {
		return nil, err
	}
	return newSQLRows(result)
}

type sqlStmt struct {
	connection *Connection
	query      string
}

func (s *sqlStmt) Close() error {
	return nil
}

func (s *sqlStmt) NumInput() int {
	return -1
}

func (s *sqlStmt) Exec(args []driver.Value) (driver.Result, error) {
	if err := rejectValues(args); err != nil {
		return nil, err
	}
	affected, err := s.connection.ExecuteNonQuery(s.query)
	if err != nil {
		return nil, err
	}
	return sqlResult(affected), nil
}

func (s *sqlStmt) Query(args []driver.Value) (driver.Rows, error) {
	if err := rejectValues(args); err != nil {
		return nil, err
	}
	result, err := s.connection.Execute(s.query)
	if err != nil {
		return nil, err
	}
	return newSQLRows(result)
}

func (s *sqlStmt) ExecContext(ctx context.Context, args []driver.NamedValue) (driver.Result, error) {
	if err := checkContext(ctx); err != nil {
		return nil, err
	}
	if err := rejectNamedArgs(args); err != nil {
		return nil, err
	}
	return s.Exec(nil)
}

func (s *sqlStmt) QueryContext(ctx context.Context, args []driver.NamedValue) (driver.Rows, error) {
	if err := checkContext(ctx); err != nil {
		return nil, err
	}
	if err := rejectNamedArgs(args); err != nil {
		return nil, err
	}
	return s.Query(nil)
}

type sqlRows struct {
	result  *Result
	columns []string
}

func newSQLRows(result *Result) (*sqlRows, error) {
	columns, err := result.Columns()
	if err != nil {
		_ = result.Close()
		return nil, err
	}
	return &sqlRows{result: result, columns: columns}, nil
}

func (r *sqlRows) Columns() []string {
	columns := make([]string, len(r.columns))
	copy(columns, r.columns)
	return columns
}

func (r *sqlRows) Close() error {
	return r.result.Close()
}

func (r *sqlRows) Next(dest []driver.Value) error {
	ok, err := r.result.Next()
	if err != nil {
		return err
	}
	if !ok {
		return io.EOF
	}

	for i := range r.columns {
		value, err := r.result.Value(i)
		if err != nil {
			return err
		}
		dest[i] = value
	}
	return nil
}

type sqlResult int64

func (r sqlResult) LastInsertId() (int64, error) {
	return 0, errors.New("sonnetdb: LastInsertId is not supported")
}

func (r sqlResult) RowsAffected() (int64, error) {
	return int64(r), nil
}

func checkContext(ctx context.Context) error {
	select {
	case <-ctx.Done():
		return ctx.Err()
	default:
		return nil
	}
}

func rejectNamedArgs(args []driver.NamedValue) error {
	if len(args) == 0 {
		return nil
	}
	return errors.New("sonnetdb: SQL parameters are not supported by the native ABI yet")
}

func rejectValues(args []driver.Value) error {
	if len(args) == 0 {
		return nil
	}
	return errors.New("sonnetdb: SQL parameters are not supported by the native ABI yet")
}
